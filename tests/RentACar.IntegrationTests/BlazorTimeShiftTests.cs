using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Crm;
using RentACar.Application.InvoicePeriods;
using RentACar.Application.Finance;
using RentACar.Infrastructure.Migrations;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// <c>FixBlazorSaatKaymasi</c> veri migration'ı GERÇEK DB'de. Blazor formları <c>datetime-local</c> İstanbul duvar saatini
/// UTC etiketiyle sakladı (09:00 → 09:00Z); düzeltme Blazor dönemi alanlarını 3 saat geri alır (09:00Z → 06:00Z).
///
/// <para>Zaman çizgisi testte kurulur: önce "Blazor dönemi" kayıtları gerçek servislerle yazılır (denetim kaydı
/// doğal olarak oluşur), sonra kesiş anı alınır, sonra "SPA dönemi" yazımları. Fonksiyonlar owner (racar_owner —
/// NOBYPASSRLS) bağlantısında, EF migrator'ı gibi koşar; değerler racar_app ile firmanın kendi bağlamında okunur.
/// Paylaşılan test DB'sinde başka testlerin verisine dokunulmasın diye her çağrı firma süzgeciyle yapılır ve her test
/// kendi düzeltmesini sonunda geri alır.</para>
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler testte elle yazılır — İstanbul 2016'dan beri sabit UTC+3, Blazor değeri
/// <c>x</c> ise doğrusu <c>x - 3 saat</c>; migration'ın SQL formülünden türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class BlazorTimeShiftTests(PostgresFixture fx)
{
    private static readonly TimeSpan Shift = TimeSpan.FromHours(-3);

    [Fact]
    public async Task Blazor_donemi_alanlari_kayar_SPA_ve_kopyalar_dogru_karar_tekrar_zararsiz_geri_alinir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var reservations = sp.GetRequiredService<ReservationService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var quotations = sp.GetRequiredService<QuotationService>();
        var plans = sp.GetRequiredService<InvoicePeriodPlanService>();
        var customer = await TestCustomer.NewAsync(sp, "Saat");

        // ---- Blazor dönemi ----
        var q1Bas = TestZaman.DaysLater(10, 9);
        var q1 = await quotations.CreateAsync(new QuotationInput
        { MusteriId = customer, VehicleId = await TestVehicle.NewAsync(sp), BasTar = q1Bas, BitTar = q1Bas.AddDays(3), GunlukUcret = 100m });

        var r1Bas = TestZaman.DaysLater(20, 9);
        var r1 = await reservations.CreateAsync(Booking(customer, await TestVehicle.NewAsync(sp), r1Bas, r1Bas.AddDays(2)));

        var secBas = TestZaman.DaysLater(30, 9).AddSeconds(17); // saniyeli: datetime-local üretmez → form değil
        var rSec = await reservations.CreateAsync(Booking(customer, await TestVehicle.NewAsync(sp), secBas, secBas.AddDays(2)));

        var tutBas = TestZaman.DaysLater(40, 9);
        var rTut = await reservations.CreateAsync(Booking(customer, await TestVehicle.NewAsync(sp), tutBas, tutBas.AddDays(2)));

        var k1Bas = TestZaman.DaysLater(-3, 9);
        var k1Vehicle = await TestVehicle.NewAsync(sp);
        var k1 = await rentals.CreateDirectAsync(Booking(customer, k1Vehicle, k1Bas, k1Bas.AddDays(6)));
        Assert.True(await rentals.DeliverAsync(k1, 1000, 4));

        var k2Bas = TestZaman.DaysLater(-6, 9);
        var k2Return = TestZaman.DaysLater(-1, 8);
        var k2Vehicle = await TestVehicle.NewAsync(sp);
        var k2 = await rentals.CreateDirectAsync(Booking(customer, k2Vehicle, k2Bas, k2Bas.AddDays(5)));
        Assert.True(await rentals.DeliverAsync(k2, 1000, 4));
        Assert.True(await rentals.ReturnAsync(k2, 1500, 4, k2Return));

        var k3Bas = TestZaman.DaysLater(-2, 9);
        var k3 = await rentals.CreateDirectAsync(Booking(customer, await TestVehicle.NewAsync(sp), k3Bas, k3Bas.AddDays(40)));
        await plans.EnsurePlanAsync(k3);

        // Ham SQL ile (denetimsiz) değişmiş alan: denetimdeki son değer güncel değil → dokunulmaz.
        await ExecAppAsync(tenant, """update "Reservations" set "BasTar" = "BasTar" + interval '1 hour', "BitTar" = "BitTar" + interval '1 hour' where "Id" = @id""", rTut);

        var cutover = await CutoverAsync();

        // ---- SPA dönemi ----
        var rQ = await quotations.AcceptAsync(q1);                  // tekliften kopya → teklifin kararı (kayar)
        var kR1 = await reservations.ConvertToRentalAsync(r1);      // rezervasyondan kopya → rezervasyonun kararı (kayar)
        var spaBas = TestZaman.DaysLater(50, 6);
        var rSpa = await reservations.CreateAsync(Booking(customer, await TestVehicle.NewAsync(sp), spaBas, spaBas.AddDays(2)));
        var k1Return = TestZaman.DaysLater(-1, 12);
        Assert.True(await rentals.ReturnAsync(k1, 1200, 4, k1Return)); // SPA dönüşü: gerçek dönüş + km kaydı kaymaz
        var k3NewEnd = k3Bas.AddDays(50);
        Assert.True(await rentals.ExtendAsync(k3, k3NewEnd));        // SPA uzatması: bitiş kaymaz, başlangıç kayar
        await plans.EnsurePlanAsync(k3);                             // plan yeniden: iç sınırlar başlangıcı, son sınır bitişi izler

        try
        {
            Assert.Equal(18, await ApplyAsync(cutover, serverWasUtc: false, tenant));

            // Teklif ve kopyaları.
            Assert.Equal(q1Bas + Shift, await ReadAsync(tenant, "Quotations", "BasTar", q1));
            Assert.Equal(q1Bas.AddDays(3) + Shift, await ReadAsync(tenant, "Quotations", "BitTar", q1));
            Assert.Equal(q1Bas + Shift, await ReadAsync(tenant, "Reservations", "BasTar", rQ));
            Assert.Equal(q1Bas.AddDays(3) + Shift, await ReadAsync(tenant, "Reservations", "BitTar", rQ));
            Assert.Equal(r1Bas + Shift, await ReadAsync(tenant, "Reservations", "BasTar", r1));
            Assert.Equal(r1Bas.AddDays(2) + Shift, await ReadAsync(tenant, "Reservations", "BitTar", r1));
            Assert.Equal(r1Bas + Shift, await ReadAsync(tenant, "Rentals", "BasTar", kR1));
            Assert.Equal(r1Bas.AddDays(2) + Shift, await ReadAsync(tenant, "Rentals", "BitTar", kR1));

            // Dokunulmayanlar: saniyeli, denetimsiz değişmiş, SPA.
            Assert.Equal(secBas, await ReadAsync(tenant, "Reservations", "BasTar", rSec));
            Assert.Equal(tutBas.AddHours(1), await ReadAsync(tenant, "Reservations", "BasTar", rTut));
            Assert.Equal(tutBas.AddDays(2).AddHours(1), await ReadAsync(tenant, "Reservations", "BitTar", rTut));
            Assert.Equal(spaBas, await ReadAsync(tenant, "Reservations", "BasTar", rSpa));
            Assert.Equal(spaBas.AddDays(2), await ReadAsync(tenant, "Reservations", "BitTar", rSpa));

            // K1: Blazor tarihleri kayar; SPA dönüşü ve km kaydı kaymaz.
            Assert.Equal(k1Bas + Shift, await ReadAsync(tenant, "Rentals", "BasTar", k1));
            Assert.Equal(k1Bas.AddDays(6) + Shift, await ReadAsync(tenant, "Rentals", "BitTar", k1));
            Assert.Equal(k1Return, await ReadAsync(tenant, "Rentals", "GercekDonusTar", k1));
            Assert.Equal(new[] { k1Return }, await KmLogDatesAsync(tenant, k1Vehicle));

            // K2: Blazor dönüşü ve ondan türeyen km kaydı kayar.
            Assert.Equal(k2Bas + Shift, await ReadAsync(tenant, "Rentals", "BasTar", k2));
            Assert.Equal(k2Bas.AddDays(5) + Shift, await ReadAsync(tenant, "Rentals", "BitTar", k2));
            Assert.Equal(k2Return + Shift, await ReadAsync(tenant, "Rentals", "GercekDonusTar", k2));
            Assert.Equal(new[] { k2Return + Shift }, await KmLogDatesAsync(tenant, k2Vehicle));

            // K3: başlangıç kayar, SPA'da uzatılan bitiş kaymaz; dönem planı aynı kuralı izler.
            Assert.Equal(k3Bas + Shift, await ReadAsync(tenant, "Rentals", "BasTar", k3));
            Assert.Equal(k3NewEnd, await ReadAsync(tenant, "Rentals", "BitTar", k3));
            Assert.Equal(
                new[] { (k3Bas + Shift, k3Bas.AddMonths(1) + Shift), (k3Bas.AddMonths(1) + Shift, k3NewEnd) },
                await PeriodsAsync(tenant, k3));

            await AssertForcedAsync();

            // Aynı kesişle ikinci çalıştırma hiçbir şey değiştirmez.
            Assert.Equal(0, await ApplyAsync(cutover, serverWasUtc: false, tenant));
            Assert.Equal(q1Bas + Shift, await ReadAsync(tenant, "Quotations", "BasTar", q1));
            Assert.Equal(k2Return + Shift, await ReadAsync(tenant, "Rentals", "GercekDonusTar", k2));
            var report = await ReportAsync(cutover, serverWasUtc: false, tenant);
            Assert.DoesNotContain(report, r => r.Kaydir);
            Assert.Equal(18, report.Where(r => r.Gerekce == "zaten düzeltildi (defterde)").Sum(r => r.Adet));

            // FARKLI kesişle yeniden uygulama reddedilir (SPA kayıtlarını kaydırabilirdi).
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(cutover.AddDays(1), serverWasUtc: false, tenant));
            Assert.Contains("FARKLI bir kesişle", ex.MessageText);

            // Geri alma: her şey eski hâline.
            Assert.Equal(18, await RevertAsync(tenant));
            Assert.Equal(q1Bas, await ReadAsync(tenant, "Quotations", "BasTar", q1));
            Assert.Equal(r1Bas, await ReadAsync(tenant, "Rentals", "BasTar", kR1));
            Assert.Equal(k2Return, await ReadAsync(tenant, "Rentals", "GercekDonusTar", k2));
            Assert.Equal(new[] { k2Return }, await KmLogDatesAsync(tenant, k2Vehicle));
            Assert.Equal(
                new[] { (k3Bas, k3Bas.AddMonths(1)), (k3Bas.AddMonths(1), k3NewEnd) },
                await PeriodsAsync(tenant, k3));
            Assert.Equal(0, await LedgerCountAsync(tenant));
            await AssertForcedAsync();
        }
        finally
        {
            await RevertAsync(tenant);
        }
    }

    [Fact]
    public async Task Sunucu_saatine_bagli_alan_yalniz_anahtarla_kayar_fatura_yalniz_raporlanir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;

        var zaman = TestZaman.DaysLater(-1, 10);
        var assist = await sp.GetRequiredService<AssistanceRequestService>().CreateAsync(new AssistanceRequestInput { Mesaj = "Lastik", Zaman = zaman });
        var invoiceDate = TestZaman.DaysLater(-2, 10);
        var invoice = await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = await TestCustomer.NewAsync(sp, "Fatura"), NetTutar = 100m, KdvOrani = 0.20m, Tarih = invoiceDate });

        var cutover = await CutoverAsync();
        try
        {
            var report = await ReportAsync(cutover, serverWasUtc: false, tenant);
            var assistRow = Assert.Single(report, r => r.Tablo == "AssistansTalepleri");
            Assert.False(assistRow.Kaydir);
            Assert.StartsWith("sunucu saat dilimine bağlı", assistRow.Gerekce);
            var invoiceRow = Assert.Single(report, r => r.Tablo == "Invoices");
            Assert.False(invoiceRow.Kaydir);
            Assert.StartsWith("değişmez tablo (fatura)", invoiceRow.Gerekce);

            Assert.Equal(1, await ApplyAsync(cutover, serverWasUtc: true, tenant));
            Assert.Equal(zaman + Shift, await ReadAsync(tenant, "AssistansTalepleri", "Zaman", assist));
            Assert.Equal(invoiceDate, await ReadAsync(tenant, "Invoices", "Tarih", invoice)); // değişmez tabloya dokunulmaz
        }
        finally
        {
            await RevertAsync(tenant);
        }
        Assert.Equal(zaman, await ReadAsync(tenant, "AssistansTalepleri", "Zaman", assist));
    }

    [Fact]
    public async Task Kaydirma_acik_kiralari_cakistiracaksa_hicbir_sey_degismez_ve_raporda_gorunur()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var customer = await TestCustomer.NewAsync(sp, "Cakisma");
        var vehicle = await TestVehicle.NewAsync(sp);

        var blazorBas = TestZaman.DaysLater(5, 12);
        var blazor = await rentals.CreateDirectAsync(Booking(customer, vehicle, blazorBas, blazorBas.AddDays(3)));
        var cutover = await CutoverAsync();
        // SPA kirası Blazor kirasının bir saat öncesinde biter; Blazor kirası 3 saat geri kayınca çakışırdı.
        var spa = await rentals.CreateDirectAsync(Booking(customer, vehicle, TestZaman.DaysLater(2, 9), blazorBas.AddHours(-1)));

        var conflict = Assert.Single(await ReportAsync(cutover, serverWasUtc: false, tenant), r => r.Kolon == "Period");
        Assert.Equal(1, conflict.Adet);
        Assert.StartsWith("ÇAKIŞMA", conflict.Gerekce);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(cutover, serverWasUtc: false, tenant));
        Assert.Contains("çakışıyor", ex.MessageText);
        Assert.Equal(blazorBas, await ReadAsync(tenant, "Rentals", "BasTar", blazor));
        Assert.Equal(blazorBas.AddHours(-1), await ReadAsync(tenant, "Rentals", "BitTar", spa));
        Assert.Equal(0, await LedgerCountAsync(tenant));
        await AssertForcedAsync();
    }

    [Fact]
    public async Task Migration_kapisi_ayarsiz_dokunmaz_ayarla_uygular_Down_geri_alir_uygulama_defteri_okuyamaz()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var bas = TestZaman.DaysLater(15, 9);
        var reservation = await sp.GetRequiredService<ReservationService>().CreateAsync(
            Booking(await TestCustomer.NewAsync(sp, "Kapi"), await TestVehicle.NewAsync(sp), bas, bas.AddDays(2)));
        var cutover = await CutoverAsync();

        var migration = new FixBlazorSaatKaymasi();
        try
        {
            // Ayar yok: yalnız tanımlar (yeniden) kurulur, veriye dokunulmaz.
            await RunMigrationAsync(migration.UpOperations, settings: new());
            Assert.Equal(bas, await ReadAsync(tenant, "Reservations", "BasTar", reservation));

            // Ayarla (bu firma): Blazor dönemi kaydı kayar.
            await RunMigrationAsync(migration.UpOperations, settings: new()
            {
                ["racar.saat_kaymasi_kesis"] = cutover.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", System.Globalization.CultureInfo.InvariantCulture),
                ["racar.saat_kaymasi_kiraci"] = tenant.ToString(),
            });
            Assert.Equal(bas + Shift, await ReadAsync(tenant, "Reservations", "BasTar", reservation));
            Assert.Equal(bas.AddDays(2) + Shift, await ReadAsync(tenant, "Reservations", "BitTar", reservation));
            await AssertForcedAsync();

            // Uygulama rolü defteri göremez.
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var conn = new NpgsqlConnection(fx.AppConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("""select count(*) from "SaatKaymasiDuzeltmeleri" """, conn);
                await cmd.ExecuteScalarAsync();
            });
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);

            // Down: geri alır ve kaldırır.
            await RunMigrationAsync(migration.DownOperations, settings: new());
            Assert.Equal(bas, await ReadAsync(tenant, "Reservations", "BasTar", reservation));
            Assert.Equal(bas.AddDays(2), await ReadAsync(tenant, "Reservations", "BitTar", reservation));
            Assert.False(await OwnerScalarAsync<bool>("""select to_regclass('"SaatKaymasiDuzeltmeleri"') is not null"""));
            await AssertForcedAsync();
        }
        finally
        {
            // Paylaşılan DB'yi migrate edilmiş hâline geri getir (ayarsız: veriye dokunmaz).
            await RunMigrationAsync(migration.UpOperations, settings: new());
        }
        Assert.Equal(bas, await ReadAsync(tenant, "Reservations", "BasTar", reservation));
    }

    [Fact]
    public void Kuru_calistirma_betigi_migration_SQL_ile_birebir()
    {
        var path = Path.Combine(RepoRoot(), "scripts", "saat-kaymasi", "kuru-calistir.sql");
        var expected = DryRunScript();
        if (Environment.GetEnvironmentVariable("RACAR_SAAT_BETIK_GUNCELLE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }
        Assert.True(File.Exists(path), "kuru çalışma betiği yok: RACAR_SAAT_BETIK_GUNCELLE=1 ile bu testi koşun");
        Assert.True(expected == File.ReadAllText(path),
            "scripts/saat-kaymasi/kuru-calistir.sql migration SQL'inden farklı: RACAR_SAAT_BETIK_GUNCELLE=1 ile yeniden üretin");
    }

    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Kuru çalışma betiği: migration'ın tanım SQL'i + rapor sorguları, tek transaction, ROLLBACK.</summary>
    internal static string DryRunScript()
    {
        var sb = new StringBuilder();
        sb.Append("""
            -- Blazor saat kayması: KURU ÇALIŞMA. Veriye dokunmaz: tek transaction, sonunda ROLLBACK.
            -- ÜRETİLMİŞ DOSYA, elle düzenlemeyin. Kaynak: FixBlazorSaatKaymasi migration'ının SQL'i
            -- (BlazorTimeShiftTests birebir kilitler; yeniden üretmek için RACAR_SAAT_BETIK_GUNCELLE=1 ile o testi koşun).
            --
            -- Kullanım (ayrıntı: docs/ops/blazor-saat-kaymasi.md):
            --   psql "<bağlantı>" -v kesis='2026-10-20 03:00:00+03' -f scripts/saat-kaymasi/kuru-calistir.sql
            --   isteğe bağlı:  -v sunucu_utc=true   FormParse.Date alanları da (Blazor sunucusu UTC saat dilimindeydiyse)
            --                  -v kiraci=<uuid>     yalnız bir firma
            --                  -v detay=1           kaydırılacak her alanı tek tek listele
            -- Rol: süper kullanıcı (tablo kilidi almaz) ya da racar_owner (RLS NO FORCE parantezi bu transaction'da
            -- kalır; ROLLBACK geri alır). Kesişi MUTLAKA ofsetle verin: ofsetsiz değer oturum saat dilimiyle yorumlanır.

            \set ON_ERROR_STOP on
            \if :{?kesis}
            \else
              \echo 'kesis değişkeni gerekli, ör.: -v kesis=''2026-10-20 03:00:00+03'''
              \quit
            \endif
            \if :{?sunucu_utc}
            \else
              \set sunucu_utc false
            \endif
            \if :{?kiraci}
            \else
              \set kiraci ''
            \endif

            BEGIN;


            """);
        foreach (var sql in FixBlazorSaatKaymasi.DefinitionSql)
            sb.Append(sql.TrimEnd()).Append("\n\n");
        sb.Append("""
            SELECT rc_time_shift_unforce() AS gecici_no_force;

            \echo '== Özet: tablo / kolon / karar / gerekçe =='
            SELECT * FROM rc_time_shift_report(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid);

            \echo '== Toplam (kaydırılacak) =='
            SELECT count(*) FILTER (WHERE c."Kaydir") AS alan,
                   count(DISTINCT (c."Tablo", c."KayitId")) FILTER (WHERE c."Kaydir") AS kayit,
                   count(DISTINCT c."TenantId") FILTER (WHERE c."Kaydir") AS firma,
                   min(c."Eski") FILTER (WHERE c."Kaydir") AS en_erken,
                   max(c."Eski") FILTER (WHERE c."Kaydir") AS en_gec
            FROM rc_time_shift_classify(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid) c;

            \echo '== Kaydırma sonrası çakışacak açık kiralar (boş olmalı; doluysa uygulama reddedilir) =='
            SELECT * FROM rc_time_shift_conflicts(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid);

            \if :{?detay}
            \echo '== Ayrıntı (kaydırılacak alanlar) =='
            SELECT * FROM rc_time_shift_classify(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid) c
            WHERE c."Kaydir"
            ORDER BY c."TenantId", c."Tablo", c."Kolon", c."Eski";
            \endif

            ROLLBACK;

            """);
        return sb.ToString();
    }

    private static BookingInput Booking(Guid customer, Guid vehicle, DateTimeOffset start, DateTimeOffset end) => new()
    { MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = end, GunlukUcret = 100m };

    /// <summary>Kesiş anı: önceki yazımlardan kesin sonra, sonrakilerden kesin önce.</summary>
    private static async Task<DateTimeOffset> CutoverAsync()
    {
        await Task.Delay(20);
        var cutover = DateTimeOffset.UtcNow;
        await Task.Delay(20);
        return cutover;
    }

    private async Task<int> ApplyAsync(DateTimeOffset cutover, bool serverWasUtc, Guid tenant)
        => await OwnerScalarAsync<int>("select rc_time_shift_apply(@c, @u, @t)", ("c", cutover), ("u", serverWasUtc), ("t", tenant));

    private async Task<int> RevertAsync(Guid tenant)
        => await OwnerScalarAsync<int>("select rc_time_shift_revert(@t)", ("t", tenant));

    private async Task<long> LedgerCountAsync(Guid tenant)
        => await OwnerScalarAsync<long>("""select count(*) from "SaatKaymasiDuzeltmeleri" where "TenantId" = @t""", ("t", tenant));

    private sealed record ReportRow(string Tablo, string Kolon, bool Kaydir, string Gerekce, long Adet);

    private async Task<List<ReportRow>> ReportAsync(DateTimeOffset cutover, bool serverWasUtc, Guid tenant)
    {
        await using var conn = new NpgsqlConnection(fx.OwnerConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand(
            """select "Tablo", "Kolon", "Kaydir", "Gerekce", "Adet" from rc_time_shift_report(@c, @u, @t)""", conn, tx);
        cmd.Parameters.AddWithValue("c", cutover);
        cmd.Parameters.AddWithValue("u", serverWasUtc);
        cmd.Parameters.AddWithValue("t", tenant);
        var rows = new List<ReportRow>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                rows.Add(new ReportRow(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetString(3), r.GetInt64(4)));
        await tx.CommitAsync();
        return rows;
    }

    private async Task<T> OwnerScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(fx.OwnerConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = (T)(await cmd.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return result;
    }

    /// <summary>Migration'ın SQL işlemleri owner bağlantısında tek transaction'da (EF migrator'ı gibi), ayarlar transaction'a yerel.</summary>
    private async Task RunMigrationAsync(
        IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations,
        Dictionary<string, string> settings)
    {
        await using var conn = new NpgsqlConnection(fx.OwnerConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var (name, value) in settings)
        {
            await using var set = new NpgsqlCommand("select set_config(@n, @v, true)", conn, tx);
            set.Parameters.AddWithValue("n", name);
            set.Parameters.AddWithValue("v", value);
            await set.ExecuteScalarAsync();
        }
        foreach (var op in operations)
        {
            var sql = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(op).Sql;
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private async Task AssertForcedAsync()
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            select relname from pg_class
            where relname = any(array['AuditLogs','Quotations','Reservations','Rentals','FaturaDonemleri','VehicleKmLoglari',
                                      'AssistansTalepleri','Baflar','SiteTalepleri','Invoices'])
              and not (relrowsecurity and relforcerowsecurity)
            """, conn);
        var unforced = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) unforced.Add(r.GetString(0));
        Assert.Empty(unforced);
    }

    private async Task<DateTimeOffset> ReadAsync(Guid tenant, string table, string column, Guid id)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, tenant);
        await using var cmd = new NpgsqlCommand($"""select "{column}" from "{table}" where "Id" = @id""", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        var value = await cmd.ExecuteScalarAsync();
        Assert.NotNull(value);
        return new DateTimeOffset(Assert.IsType<DateTime>(value), TimeSpan.Zero);
    }

    private async Task<List<DateTimeOffset>> KmLogDatesAsync(Guid tenant, Guid vehicle)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, tenant);
        await using var cmd = new NpgsqlCommand(
            """select "Tarih" from "VehicleKmLoglari" where "VehicleId" = @v and "Kaynak" = 1 order by "Tarih" """, conn, tx);
        cmd.Parameters.AddWithValue("v", vehicle);
        var dates = new List<DateTimeOffset>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) dates.Add(new DateTimeOffset(r.GetDateTime(0), TimeSpan.Zero));
        return dates;
    }

    private async Task<List<(DateTimeOffset, DateTimeOffset)>> PeriodsAsync(Guid tenant, Guid rental)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, tenant);
        await using var cmd = new NpgsqlCommand(
            """select "DonemBas", "DonemBit" from "FaturaDonemleri" where "RentalId" = @r order by "DonemSira" """, conn, tx);
        cmd.Parameters.AddWithValue("r", rental);
        var periods = new List<(DateTimeOffset, DateTimeOffset)>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                periods.Add((new DateTimeOffset(r.GetDateTime(0), TimeSpan.Zero), new DateTimeOffset(r.GetDateTime(1), TimeSpan.Zero)));
        return periods;
    }

    /// <summary>Servisi ve denetimi atlayan ham yazım (racar_app + firma bağlamı).</summary>
    private async Task ExecAppAsync(Guid tenant, string sql, Guid id)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, tenant);
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        await tx.CommitAsync();
    }

    private static async Task SetTenantAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenant)
    {
        await using var cmd = new NpgsqlCommand("select set_config('app.tenant_id', @t, true)", conn, tx);
        cmd.Parameters.AddWithValue("t", tenant.ToString());
        await cmd.ExecuteScalarAsync();
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("RentACar.slnx bulunamadı");
    }
}
