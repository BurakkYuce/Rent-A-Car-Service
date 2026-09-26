using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Low temizliği B — para dokunan Low maddeleri. BAĞIMSIZ ORACLE (elle):
/// R04: göreli başlangıç (TestZaman, whole-second) + 90 g × 100; D1 = (bir sonraki ayın aynı günü − başlangıç)
/// takvim günü × 100 (dönem faturası brütü; ay uzunluğu takvimden elle, servis kodundan DEĞİL).
/// N4: 65 gün önce başlamış 90 günlük kira; vadesi geçmiş 2 dönem — kira kilit beklerken iptal edilirse 0 fatura.
/// RentalsApi: yabancı/olmayan müşteri-araç kimliği → 400 <c>validation</c>, hiçbir kira/rezervasyon yazılmaz.
/// </summary>
[Collection("postgres")]
public sealed class LowTemizligiBTests(PostgresFixture fx)
{
    // Sabit tarih yok (TestTarihBombasiTests): göreli, whole-second hizalı başlangıç.
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(10);

    /// <summary>Oracle: first period = calendar days from start to the same day next month, × 100 per day.</summary>
    private static decimal FirstPeriodGross(DateTimeOffset start) =>
        (start.AddMonths(1).UtcDateTime.Date - start.UtcDateTime.Date).Days * 100m;

    private static async Task<(Guid kira, Guid cari)> RentalAsync(IServiceProvider sp, string plate, DateTimeOffset start,
        bool periodic = false)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "LB", Soyad = "M" });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = start, BitTar = start.AddDays(90), GunlukUcret = 100m,
            DonemselFaturalama = periodic
        });
        return (id, m);
    }

    // ------------------------------------------------------------ R04

    [Fact]
    public async Task R04_rowkey_farkli_tutarla_onden_alinmissa_409_mevcut_ve_tahsilat_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, account) = await RentalAsync(sp, "34 LB 01", Start);
        var firstPeriod = FirstPeriodGross(Start);
        var cash = sp.GetRequiredService<CashService>();

        // Blazor ham anahtar yolu: AYNI kiraya 1 TL, dönem tahsilatının deterministik anahtarıyla.
        await cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rental, Tutar = 1m, Doviz = "TRY", Kur = 1m, Hesap = LedgerAccountType.Kasa,
            Aciklama = "onden", IslemAnahtari = CashService.RowKey(rental, 1)
        });

        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() => sp.GetRequiredService<PeriodCollectionService>()
            .IssueAndCollectDetailAsync(rental, 1, true, LedgerAccountType.Kasa));
        Assert.NotNull(ex.Existing);
        Assert.False(ex.Existing!.AyniIcerik);
        Assert.Equal(1m, ex.Existing.Tutar);
        Assert.Contains("YAZILMADI", ex.Message);

        // Fatura kesildi (D1 borç), yalnız ön-alınan 1 TL alacak: bakiye D1 − 1 — dönem tahsilatı YOK.
        Assert.Equal(firstPeriod - 1m, await cash.GetAccountBalanceAsync(account));
    }

    [Fact]
    public async Task R04_rowkey_ayni_tutar_doviz_kurla_alinmissa_sessiz_basari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, account) = await RentalAsync(sp, "34 LB 02", Start);
        var firstPeriod = FirstPeriodGross(Start);
        var cash = sp.GetRequiredService<CashService>();
        await cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rental, Tutar = firstPeriod, Doviz = "TRY", Kur = 1m, Hesap = LedgerAccountType.Kasa,
            IslemAnahtari = CashService.RowKey(rental, 1)
        });

        var (_, written) = await sp.GetRequiredService<PeriodCollectionService>()
            .IssueAndCollectDetailAsync(rental, 1, true, LedgerAccountType.Kasa);
        Assert.False(written);
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account)); // D1 borç − D1 alacak
    }

    // ------------------------------------------------------------ N4

    [Fact]
    public async Task N4_job_kilit_beklerken_iptal_edilen_kiraya_donem_faturasi_kesmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s =>
        { s.DonemselFaturalamaJob = true; s.DonemselOtomatikTahsilat = true; });
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var (rental, _) = await RentalAsync(sp, "34 LB 03", start, periodic: true);
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var lockKey = $"fatura:{tenant}:{rental}";

        // Kira iptali (KiraKilitleri sırası): önce fatura advisory kilidi, sonra durum yazımı — commit'e dek tutulur.
        await using var cancel = await factory.CreateDbContextAsync();
        await using var tx = await cancel.Database.BeginTransactionAsync();
        await cancel.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 42))");
        var r = await cancel.Rentals.FirstAsync(x => x.Id == rental);
        r.Durum = RentalStatus.Iptal;
        await cancel.SaveChangesAsync();

        // Job adayları kilitsiz okur (kira hâlâ Kirada görünür) → ilk dönemde kilide takılır.
        var job = Task.Run(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            return await PeriodInvoiceGenerator.RunAsync(db, tenant, DateTimeOffset.UtcNow);
        });
        Assert.True(await WaitsForLockAsync(lockKey), "job kilide ulaşmadı — yarış kurulamadı");
        await tx.CommitAsync();

        var result = await job;
        Assert.Equal(0, result.Kesilen);
        Assert.Equal(0, result.Tahsilat);
        Assert.Contains(result.Atlananlar, a => a.Contains("iptal"));

        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.Invoices.AnyAsync(i => i.KaynakKiraId == rental || i.RentalId == rental));
        Assert.False(await check.CashTransactions.AnyAsync(c => c.RentalId == rental));
        Assert.All(await check.FaturaDonemleri.Where(d => d.RentalId == rental).ToListAsync(),
            d => Assert.Equal(InvoicePeriodStatus.Planlandi, d.Durum));
    }

    /// <summary>Verilen advisory anahtarı için BEKLEYEN (granted=false) bir kilit görünene dek bekler (≤10 sn).</summary>
    private async Task<bool> WaitsForLockAsync(string key)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 200; i++)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks l WHERE l.locktype = 'advisory' AND NOT l.granted " +
                "AND ((l.classid::bigint << 32) | l.objid::bigint) = hashtextextended(@k, 42)", conn);
            cmd.Parameters.AddWithValue("k", key);
            if ((long)(await cmd.ExecuteScalarAsync())! > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    // ------------------------------------------------------------ RentalsApi / ReservationsApi varlık kontrolü

    private static async Task<Guid> IdAsync(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task ExpectErrorAsync(HttpResponseMessage r, string fieldText)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal("validation", root.GetProperty("error").GetString());
        Assert.Contains(fieldText, root.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("/api/v1/rentals")]
    [InlineData("/api/v1/reservations")]
    public async Task Harici_api_yabanci_ya_da_olmayan_musteri_arac_400_ve_kayit_yazilmaz(string endpoint)
    {
        var password = Guid.NewGuid().ToString("N") + "Aa1!";
        var codeA = "lba" + Guid.NewGuid().ToString("N")[..10];
        var codeB = "lbb" + Guid.NewGuid().ToString("N")[..10];
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeA, "u", password);
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeB, "u", password);
        using var api = new ApiFactory(fx.AppConnectionString);
        var ca = await api.LoginClientAsync(codeA, "u", password);
        var cb = await api.LoginClientAsync(codeB, "u", password);

        var aVehicle = await IdAsync(await ca.PostAsJsonAsync("/api/v1/vehicles", new { plaka = "34LBA" + Random.Shared.Next(100, 999), durum = "Musait", km = 0, yakit = "Benzin" }));
        var aAccount = await IdAsync(await ca.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "A" }));
        var bVehicle = await IdAsync(await cb.PostAsJsonAsync("/api/v1/vehicles", new { plaka = "34LBB" + Random.Shared.Next(100, 999), durum = "Musait", km = 0, yakit = "Benzin" }));
        var bAccount = await IdAsync(await cb.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "B" }));

        var start = DateTimeOffset.UtcNow.AddDays(5);
        object Body(Guid m, Guid v) => new { musteriId = m, vehicleId = v, basTar = start, bitTar = start.AddDays(2), gunlukUcret = 100m };

        await ExpectErrorAsync(await ca.PostAsJsonAsync(endpoint, Body(bAccount, aVehicle)), "Müşteri");      // başka kiracının carisi
        await ExpectErrorAsync(await ca.PostAsJsonAsync(endpoint, Body(Guid.NewGuid(), aVehicle)), "Müşteri"); // olmayan cari
        await ExpectErrorAsync(await ca.PostAsJsonAsync(endpoint, Body(aAccount, bVehicle)), "Araç");         // başka kiracının aracı
        await ExpectErrorAsync(await ca.PostAsJsonAsync(endpoint, Body(aAccount, Guid.NewGuid())), "Araç");    // olmayan araç

        var list = await ca.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal(0, list.GetArrayLength());

        // Kendi kiracısının kayıtlarıyla meşru yol hâlâ 201.
        await IdAsync(await ca.PostAsJsonAsync(endpoint, Body(aAccount, aVehicle)));
    }
}
