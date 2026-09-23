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
    private static readonly DateTimeOffset Start = TestZaman.GunSonra(10);

    /// <summary>Oracle: first period = calendar days from start to the same day next month, × 100 per day.</summary>
    private static decimal FirstPeriodGross(DateTimeOffset start) =>
        (start.AddMonths(1).UtcDateTime.Date - start.UtcDateTime.Date).Days * 100m;

    private static async Task<(Guid kira, Guid cari)> KiraAsync(IServiceProvider sp, string plaka, DateTimeOffset bas,
        bool donemsel = false)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "LB", Soyad = "M" });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bas.AddDays(90), GunlukUcret = 100m,
            DonemselFaturalama = donemsel
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
        var (kira, cari) = await KiraAsync(sp, "34 LB 01", Start);
        var firstPeriod = FirstPeriodGross(Start);
        var kasa = sp.GetRequiredService<CashService>();

        // Blazor ham anahtar yolu: AYNI kiraya 1 TL, dönem tahsilatının deterministik anahtarıyla.
        await kasa.CollectAsync(new CashInput
        {
            CariId = cari, RentalId = kira, Tutar = 1m, Doviz = "TRY", Kur = 1m, Hesap = LedgerAccountType.Kasa,
            Aciklama = "onden", IslemAnahtari = CashService.RowKey(kira, 1)
        });

        var ex = await Assert.ThrowsAsync<MukerrerIslemException>(() => sp.GetRequiredService<DonemTahsilatService>()
            .KesVeTahsilEtDetayAsync(kira, 1, true, LedgerAccountType.Kasa));
        Assert.NotNull(ex.Mevcut);
        Assert.False(ex.Mevcut!.AyniIcerik);
        Assert.Equal(1m, ex.Mevcut.Tutar);
        Assert.Contains("YAZILMADI", ex.Message);

        // Fatura kesildi (D1 borç), yalnız ön-alınan 1 TL alacak: bakiye D1 − 1 — dönem tahsilatı YOK.
        Assert.Equal(firstPeriod - 1m, await kasa.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task R04_rowkey_ayni_tutar_doviz_kurla_alinmissa_sessiz_basari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (kira, cari) = await KiraAsync(sp, "34 LB 02", Start);
        var firstPeriod = FirstPeriodGross(Start);
        var kasa = sp.GetRequiredService<CashService>();
        await kasa.CollectAsync(new CashInput
        {
            CariId = cari, RentalId = kira, Tutar = firstPeriod, Doviz = "TRY", Kur = 1m, Hesap = LedgerAccountType.Kasa,
            IslemAnahtari = CashService.RowKey(kira, 1)
        });

        var (_, yazildi) = await sp.GetRequiredService<DonemTahsilatService>()
            .KesVeTahsilEtDetayAsync(kira, 1, true, LedgerAccountType.Kasa);
        Assert.False(yazildi);
        Assert.Equal(0m, await kasa.GetCariBalanceAsync(cari)); // D1 borç − D1 alacak
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
        var bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var (kira, _) = await KiraAsync(sp, "34 LB 03", bas, donemsel: true);
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var kilitAnahtari = $"fatura:{tenant}:{kira}";

        // Kira iptali (KiraKilitleri sırası): önce fatura advisory kilidi, sonra durum yazımı — commit'e dek tutulur.
        await using var iptal = await factory.CreateDbContextAsync();
        await using var tx = await iptal.Database.BeginTransactionAsync();
        await iptal.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({kilitAnahtari}, 42))");
        var r = await iptal.Rentals.FirstAsync(x => x.Id == kira);
        r.Durum = RentalStatus.Iptal;
        await iptal.SaveChangesAsync();

        // Job adayları kilitsiz okur (kira hâlâ Kirada görünür) → ilk dönemde kilide takılır.
        var job = Task.Run(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            return await DonemFaturaUretici.RunAsync(db, tenant, DateTimeOffset.UtcNow);
        });
        Assert.True(await KilitBekleniyorAsync(kilitAnahtari), "job kilide ulaşmadı — yarış kurulamadı");
        await tx.CommitAsync();

        var sonuc = await job;
        Assert.Equal(0, sonuc.Kesilen);
        Assert.Equal(0, sonuc.Tahsilat);
        Assert.Contains(sonuc.Atlananlar, a => a.Contains("iptal"));

        await using var kontrol = await factory.CreateDbContextAsync();
        Assert.False(await kontrol.Invoices.AnyAsync(i => i.KaynakKiraId == kira || i.RentalId == kira));
        Assert.False(await kontrol.CashTransactions.AnyAsync(c => c.RentalId == kira));
        Assert.All(await kontrol.FaturaDonemleri.Where(d => d.RentalId == kira).ToListAsync(),
            d => Assert.Equal(FaturaDonemDurum.Planlandi, d.Durum));
    }

    /// <summary>Verilen advisory anahtarı için BEKLEYEN (granted=false) bir kilit görünene dek bekler (≤10 sn).</summary>
    private async Task<bool> KilitBekleniyorAsync(string anahtar)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 200; i++)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks l WHERE l.locktype = 'advisory' AND NOT l.granted " +
                "AND ((l.classid::bigint << 32) | l.objid::bigint) = hashtextextended(@k, 42)", conn);
            cmd.Parameters.AddWithValue("k", anahtar);
            if ((long)(await cmd.ExecuteScalarAsync())! > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    // ------------------------------------------------------------ RentalsApi / ReservationsApi varlık kontrolü

    private static async Task<Guid> IdAsync(HttpResponseMessage r)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task HataBekleAsync(HttpResponseMessage r, string alanMetni)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {metin}");
        var kok = JsonDocument.Parse(metin).RootElement;
        Assert.Equal("validation", kok.GetProperty("error").GetString());
        Assert.Contains(alanMetni, kok.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("/api/v1/rentals")]
    [InlineData("/api/v1/reservations")]
    public async Task Harici_api_yabanci_ya_da_olmayan_musteri_arac_400_ve_kayit_yazilmaz(string uc)
    {
        var sifre = Guid.NewGuid().ToString("N") + "Aa1!";
        var kodA = "lba" + Guid.NewGuid().ToString("N")[..10];
        var kodB = "lbb" + Guid.NewGuid().ToString("N")[..10];
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, kodA, "u", sifre);
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, kodB, "u", sifre);
        using var api = new ApiFactory(fx.AppConnectionString);
        var ca = await api.LoginClientAsync(kodA, "u", sifre);
        var cb = await api.LoginClientAsync(kodB, "u", sifre);

        var aArac = await IdAsync(await ca.PostAsJsonAsync("/api/v1/vehicles", new { plaka = "34LBA" + Random.Shared.Next(100, 999), durum = "Musait", km = 0, yakit = "Benzin" }));
        var aCari = await IdAsync(await ca.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "A" }));
        var bArac = await IdAsync(await cb.PostAsJsonAsync("/api/v1/vehicles", new { plaka = "34LBB" + Random.Shared.Next(100, 999), durum = "Musait", km = 0, yakit = "Benzin" }));
        var bCari = await IdAsync(await cb.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "B" }));

        var bas = DateTimeOffset.UtcNow.AddDays(5);
        object Govde(Guid m, Guid v) => new { musteriId = m, vehicleId = v, basTar = bas, bitTar = bas.AddDays(2), gunlukUcret = 100m };

        await HataBekleAsync(await ca.PostAsJsonAsync(uc, Govde(bCari, aArac)), "Müşteri");      // başka kiracının carisi
        await HataBekleAsync(await ca.PostAsJsonAsync(uc, Govde(Guid.NewGuid(), aArac)), "Müşteri"); // olmayan cari
        await HataBekleAsync(await ca.PostAsJsonAsync(uc, Govde(aCari, bArac)), "Araç");         // başka kiracının aracı
        await HataBekleAsync(await ca.PostAsJsonAsync(uc, Govde(aCari, Guid.NewGuid())), "Araç");    // olmayan araç

        var liste = await ca.GetFromJsonAsync<JsonElement>(uc);
        Assert.Equal(0, liste.GetArrayLength());

        // Kendi kiracısının kayıtlarıyla meşru yol hâlâ 201.
        await IdAsync(await ca.PostAsJsonAsync(uc, Govde(aCari, aArac)));
    }
}
