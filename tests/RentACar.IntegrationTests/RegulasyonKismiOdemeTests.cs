using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-14 — MTV/Muayene KISMİ ÖDEME (PARA). Bağımsız oracle: kalan bakiyeler ve defter toplamları
/// senaryodan ELLE hesaplanır (1000 − 400 = 600 sabiti testte yazılıdır, koddan okunmaz).
///
/// <para><b>Kilitlenen sözleşmeler:</b> (1) HER kısmi adım kendi içinde dengeli defter yazar —
/// kapanışta toplam denkleşmesi yetmez; (2) aşım reddedilir (kalan negatife düşemez);
/// (3) çift-gönderim bakiyeyi DEĞİŞTİRMEZ; (4) TRY dışı para birimi reddedilir.</para>
/// </summary>
[Collection("postgres")]
public sealed class RegulasyonKismiOdemeTests(PostgresFixture fx)
{
    private static async Task<(IServiceProvider sp, Guid vehicleId)> SeedAsync(IServiceScope scope, string plate)
    {
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        return (sp, v);
    }

    /// <summary>Defter satırlarını doğrudan DB'den okur — servisin kendi raporundan değil.</summary>
    private static async Task<List<AccountLedgerEntry>> LedgerAsync(IServiceProvider sp, string sourceType)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == sourceType)
            .OrderBy(e => e.EntryDateUtc).ThenBy(e => e.Direction)
            .ToListAsync();
    }

    // ---------------- MTV ----------------

    [Fact]
    public async Task Mtv_iki_kismi_odeme_kalani_dogru_dusurur_ve_HER_ADIM_dengeli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 KM 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        // Açılışta kalan = tutar (ELLE: 1000)
        Assert.Equal(1000m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);

        var s1 = await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 400m, EvrakNo = " EV-1 ", IslemYapan = "Ali", IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(1, s1.Sira);
        Assert.Equal(400m, s1.Tutar);
        Assert.Equal(600m, s1.Kalan);      // ELLE: 1000 − 400
        Assert.False(s1.Odendi);

        var s2 = await reg.PayMtvAsync(mtv, LedgerAccountType.Banka,
            payment: new RegulasyonOdemeInput { Tutar = 600m, EvrakNo = "EV-2", IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(2, s2.Sira);
        Assert.Equal(0m, s2.Kalan);        // ELLE: 600 − 600
        Assert.True(s2.Odendi);

        var rec = (await reg.ListMtvAsync()).Single(x => x.Id == mtv);
        Assert.True(rec.Odendi);
        Assert.Equal(0m, rec.Kalan);

        // Ödeme geçmişi: iki satır, evrak numaraları BİRBİRİNİ EZMEDİ.
        var history = await reg.ListMtvPaymentsAsync(mtv);
        Assert.Equal(2, history.Count);
        Assert.Equal("EV-1", history[0].EvrakNo);        // trim
        Assert.Equal("Ali", history[0].IslemYapan);
        Assert.Equal("EV-2", history[1].EvrakNo);
        Assert.Equal(600m, history[0].KalanSonrasi);
        Assert.Equal(0m, history[1].KalanSonrasi);
        Assert.Equal(LedgerAccountType.Kasa, history[0].Hesap);
        Assert.Equal(LedgerAccountType.Banka, history[1].Hesap);

        // DEFTER: 4 satır (adım başına 1 borç + 1 alacak). HER ADIM kendi içinde dengeli.
        var rows = await LedgerAsync(sp, "MtvOdeme");
        Assert.Equal(4, rows.Count);
        foreach (var group in rows.GroupBy(e => e.SourceId))
        {
            var debit = group.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var credit = group.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            Assert.Equal(debit, credit);
            Assert.NotEqual(0m, debit);
        }
        // 400 ve 600 AYRI AYRI postlandı (toplamları karıştırılmadı).
        var debts = rows.Where(e => e.Direction == LedgerDirection.Debit)
            .Select(e => e.Amount.Amount).OrderBy(x => x).ToArray();
        Assert.Equal([400m, 600m], debts);
        // Gider ARACA atıflı, alacak tarafı hesap (AccountRef yok).
        Assert.All(rows.Where(e => e.AccountType == LedgerAccountType.Gider), e => Assert.Equal(v, e.AccountRef));
        Assert.Contains(rows, e => e.AccountType == LedgerAccountType.Kasa);
        Assert.Contains(rows, e => e.AccountType == LedgerAccountType.Banka);

        // Gelir-gider raporu toplamı: ELLE 1000.
        Assert.Equal(1000m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);

        // Kapanmış kayda yeni ödeme YOK.
        await Assert.ThrowsAsync<ValidationException>(() => reg.PayMtvAsync(mtv, LedgerAccountType.Kasa));
    }

    [Fact]
    public async Task Mtv_tutar_verilmezse_ESKI_DAVRANIS_kalanin_tamami_odenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 KM 02");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 2000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        var s = await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa);   // eski imza, tutar YOK
        Assert.Equal(2000m, s.Tutar);
        Assert.Equal(0m, s.Kalan);
        Assert.True(s.Odendi);
        Assert.Equal(2000m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);
    }

    [Fact]
    public async Task Mtv_ASIM_reddedilir_ve_HICBIR_SEY_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 KM 03");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = Guid.NewGuid() });

        // Kalan 600 iken 700 ödemeye çalış → red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 700m, IslemAnahtari = Guid.NewGuid() }));
        Assert.Contains("kalan bakiyeyi aşamaz", ex.Message);

        // Bakiye ve defter DEĞİŞMEDİ (ELLE: hâlâ 600 ve tek ödeme).
        Assert.Equal(600m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);
        Assert.Single(await reg.ListMtvPaymentsAsync(mtv));
        Assert.Equal(2, (await LedgerAsync(sp, "MtvOdeme")).Count);

        // Sıfır / negatif tutar da reddedilir.
        await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 0m, IslemAnahtari = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = -100m }));
        Assert.Equal(600m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);
    }

    [Fact]
    public async Task Mtv_ayni_islem_anahtariyla_CIFT_GONDERIM_bakiyeyi_degistirmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 KM 04");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        var key = Guid.NewGuid();

        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 300m, IslemAnahtari = key });
        Assert.Equal(700m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);

        // AYNI anahtarla ikinci gönderim → kısmi unique index → tüm transaction geri alınır.
        await Assert.ThrowsAsync<DuplicateOperationException>(() => reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 300m, IslemAnahtari = key }));

        // ELLE: kalan hâlâ 700, tek ödeme, 2 defter satırı — çift ödeme YAZILMADI.
        Assert.Equal(700m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);
        Assert.Single(await reg.ListMtvPaymentsAsync(mtv));
        Assert.Equal(2, (await LedgerAsync(sp, "MtvOdeme")).Count);
        Assert.Equal(300m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);

        // FARKLI anahtarla ikinci kısmi ödeme normal geçer.
        var s = await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 300m, IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(400m, s.Kalan);
    }

    [Fact]
    public async Task Mtv_TRY_disi_para_birimi_REDDEDILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 KM 05");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        // ESKİ AÇIK: doviz="EUR" geçilince 1000 TL'lik MTV deftere 1000 EUR × kur yazılıyordu
        // (38.000 TL). Artık reddediliyor.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, currency: "EUR", exchangeRate: 38m));
        Assert.Contains("TRY", ex.Message);

        Assert.Empty(await LedgerAsync(sp, "MtvOdeme"));
        Assert.Equal(1000m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);

        // Boş/null/try → TRY kabul.
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, currency: null, payment: new RegulasyonOdemeInput { Tutar = 1m, IslemAnahtari = Guid.NewGuid() });
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, currency: "try", payment: new RegulasyonOdemeInput { Tutar = 1m, IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(998m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);
    }

    // ---------------- Muayene ----------------

    [Fact]
    public async Task Muayene_kismi_odeme_ve_CEZA_BORCU_ARTIRIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 MU 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var insp = await reg.AddInspectionAsync(v,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 800m);

        Assert.Equal(800m, (await reg.ListInspectionAsync()).Single(x => x.Id == insp).Kalan);

        // 1. ödeme: 200 ceza eklenir (borç 800 + 200 = 1000), 300 ödenir.
        var s1 = await reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, penalty: 200m,
            payment: new RegulasyonOdemeInput { Tutar = 300m, IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(300m, s1.Tutar);
        Assert.Equal(700m, s1.Kalan);      // ELLE: 800 + 200 − 300
        Assert.False(s1.Odendi);

        var rec1 = (await reg.ListInspectionAsync()).Single(x => x.Id == insp);
        Assert.Equal(200m, rec1.Ceza);
        Assert.Equal(700m, rec1.Kalan);

        // 2. ödeme: cezasız, kalanın tamamı.
        var s2 = await reg.PayInspectionAsync(insp, LedgerAccountType.Banka);
        Assert.Equal(700m, s2.Tutar);      // ELLE: kalanın tamamı
        Assert.Equal(0m, s2.Kalan);
        Assert.True(s2.Odendi);

        // DEFTER: 4 satır, her adım dengeli, tutarlar 300 ve 700 (toplam 1000 = ücret + ceza).
        var rows = await LedgerAsync(sp, "MuayeneOdeme");
        Assert.Equal(4, rows.Count);
        foreach (var group in rows.GroupBy(e => e.SourceId))
            Assert.Equal(
                group.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
                group.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));
        Assert.Equal([300m, 700m], rows.Where(e => e.Direction == LedgerDirection.Debit)
            .Select(e => e.Amount.Amount).OrderBy(x => x).ToArray());
        Assert.Equal(1000m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);

        var history = await reg.ListInspectionPaymentsAsync(insp);
        Assert.Equal(200m, history[0].Ceza);
        Assert.Equal(0m, history[1].Ceza);
    }

    [Fact]
    public async Task Muayene_tek_seferde_odeme_ESKI_DAVRANIS_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 MU 02");
        var reg = sp.GetRequiredService<RegulationService>();
        var insp = await reg.AddInspectionAsync(v,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 800m);

        // Eski imza: ceza 100 → tek seferde 900 ödenir (ELLE: 800 + 100).
        var s = await reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, penalty: 100m);
        Assert.Equal(900m, s.Tutar);
        Assert.True(s.Odendi);
        Assert.Equal(900m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);

        var rec = (await reg.ListInspectionAsync()).Single(x => x.Id == insp);
        Assert.Equal(100m, rec.Ceza);
        Assert.Equal(0m, rec.Kalan);

        await Assert.ThrowsAsync<ValidationException>(() => reg.PayInspectionAsync(insp, LedgerAccountType.Kasa));
    }

    [Fact]
    public async Task Muayene_asim_ve_cift_gonderim_reddi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 MU 03");
        var reg = sp.GetRequiredService<RegulationService>();
        var insp = await reg.AddInspectionAsync(v,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 500m);

        // Kalan 500, ceza yok → 600 aşım.
        await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 600m, IslemAnahtari = Guid.NewGuid() }));
        Assert.Empty(await LedgerAsync(sp, "MuayeneOdeme"));

        // Ceza 100 eklenirse tavan 600 olur → aynı tutar geçer.
        await reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, penalty: 100m,
            payment: new RegulasyonOdemeInput { Tutar = 600m, IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(0m, (await reg.ListInspectionAsync()).Single(x => x.Id == insp).Kalan);

        // Çift gönderim: kayıt kapandığı için ikinci ödeme reddedilir.
        await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 1m, IslemAnahtari = Guid.NewGuid() }));
        Assert.Equal(2, (await LedgerAsync(sp, "MuayeneOdeme")).Count);
    }

    [Fact]
    public async Task Odeme_satiri_UYGULAMADAN_degistirilemez_ve_silinemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 IM 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 500m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 100m, IslemAnahtari = Guid.NewGuid() });

        // racar_app'e UPDATE/DELETE grant'i VERİLMEDİ → mali belge yapısal olarak değişmez.
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"MtvOdemeleri\" SET \"Tutar\" = 999"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"MtvOdemeleri\""));
    }

    // ---------------- Adversarial incelemeden doğan kilitler ----------------

    [Fact]
    public async Task GELECEK_TARIHLI_odeme_reddedilir()
    {
        // H1: ödeme tarihi artık formdan geliyor. Dönem kilidi geleceği kapatmaz — 2099 tarihli
        // bir gider hiçbir dönemde mutabık olmaz. Guard GİRİŞ noktasında.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 GT 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        var insp = await reg.AddInspectionAsync(v,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 500m);

        var future = DateTimeOffset.UtcNow.AddYears(50);
        await Assert.ThrowsAsync<ValidationException>(() => reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, paymentDate: future));
        await Assert.ThrowsAsync<ValidationException>(() => reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, paymentDate: future));

        Assert.Empty(await LedgerAsync(sp, "MtvOdeme"));
        Assert.Empty(await LedgerAsync(sp, "MuayeneOdeme"));
        Assert.Equal(1000m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);

        // Geçmiş tarih SERBEST (gerçek ödeme sonradan girilebilir).
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, paymentDate: DateTimeOffset.UtcNow.AddDays(-10));
        Assert.Equal(0m, (await reg.ListMtvAsync()).Single(x => x.Id == mtv).Kalan);
    }

    [Fact]
    public async Task KISMI_odemede_islem_anahtari_ZORUNLU_tam_odemede_degil()
    {
        // M3: kısmi ödemede "aynı tutarı ikinci kez yazmak" meşru bir iş senaryosu olduğundan
        // hiçbir yapısal kısıt kazara tekrarı ayıramaz — tek ayıraç açık anahtardır. Tam ödemede
        // koruma yapısaldır (Odendi/Kalan=0 çiti), o yüzden anahtar istenmez.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 AN 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 400m }));
        Assert.Contains("işlem anahtarı zorunludur", ex.Message);
        Assert.Empty(await LedgerAsync(sp, "MtvOdeme"));

        // Boş GUID de anahtar sayılmaz.
        await Assert.ThrowsAsync<ValidationException>(() => reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = Guid.Empty }));

        // Tam ödeme (Tutar yok) anahtarsız çalışır — eski davranış korunuyor.
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa);
        Assert.True((await reg.ListMtvAsync()).Single(x => x.Id == mtv).Odendi);
    }

    [Fact]
    public async Task Yuvarlanip_sifira_dusen_tutar_TEMIZ_reddedilir()
    {
        // L5: 0,00004 pozitiflik kontrolünü geçip yuvarlandıktan sonra 0 oluyordu → DB CHECK
        // ihlali (500). Artık ÖNCE yuvarlanıyor ve ValidationException dönüyor.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 YV 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 0.00004m, IslemAnahtari = Guid.NewGuid() }));
        Assert.Contains("pozitif olmalıdır", ex.Message);
        Assert.Empty(await LedgerAsync(sp, "MtvOdeme"));
    }

    [Fact]
    public async Task Odeme_gecmisi_kayit_KAPANDIKTAN_SONRA_da_gorunur()
    {
        // M2: liste "kısmen ödenmiş" filtresiyle çekiyordu; kayıt kapanınca evrak numaraları ve
        // kasa/banka dağılımı ekrandan kayboluyordu. Toplu uç kapanmış kaydı da döndürür.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, v) = await SeedAsync(scope, "34 GC 01");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 400m, EvrakNo = "EV-1", IslemAnahtari = Guid.NewGuid() });
        await reg.PayMtvAsync(mtv, LedgerAccountType.Banka,
            payment: new RegulasyonOdemeInput { Tutar = 600m, EvrakNo = "EV-2", IslemAnahtari = Guid.NewGuid() });

        Assert.True((await reg.ListMtvAsync()).Single(x => x.Id == mtv).Odendi);
        var all = await reg.ListAllMtvPaymentsAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(["EV-1", "EV-2"], all.OrderBy(x => x.Sira).Select(x => x.EvrakNo ?? "").ToArray());

        // Muayene tarafı: ceza kalanı ücrete EŞİT bırakan senaryo (800 + 300 − 300 = 800) —
        // eski filtre bunu da "hiç ödenmemiş" sayıp gizliyordu.
        var insp = await reg.AddInspectionAsync(v,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 800m);
        await reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, penalty: 300m,
            payment: new RegulasyonOdemeInput { Tutar = 300m, IslemAnahtari = Guid.NewGuid() });
        var rec = (await reg.ListInspectionAsync()).Single(x => x.Id == insp);
        Assert.Equal(800m, rec.Kalan);
        Assert.Equal(rec.Ucret, rec.Kalan);                       // eski filtrenin kör noktası
        Assert.Single(await reg.ListAllInspectionPaymentsAsync());     // yine de görünüyor
    }

    [Fact]
    public async Task Odeme_satiri_MIGRATOR_baglantisindan_da_degistirilemez()
    {
        // M4: yalnız grant yeterli değildi. racar_owner'ı iki katman korur — (1) FORCE RLS onu da
        // süzer, (2) süzgeci aşsa bile (app.tenant_id set edilirse, ki aşağıda EDİLİYOR)
        // değişmezlik trigger'ı durdurur. Trigger olmasaydı bu senaryoda Kalan ile Σ ödeme
        // sessizce ayrışırdı. Diğer mali tablolarla aynı koruma.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var (sp, v) = await SeedAsync(scope, "34 IM 02");
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 500m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa,
            payment: new RegulasyonOdemeInput { Tutar = 100m, IslemAnahtari = Guid.NewGuid() });

        await using var owner = new Npgsql.NpgsqlConnection(fx.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var setCmd = new Npgsql.NpgsqlCommand(
            $"SELECT set_config('app.tenant_id', '{tenant}', false)", owner))
            await setCmd.ExecuteScalarAsync();

        // Satır artık owner'a GÖRÜNÜR (RLS süzgeci aşıldı) — koruma yalnız trigger'a kaldı.
        await using (var sayCmd = new Npgsql.NpgsqlCommand("SELECT count(*) FROM \"MtvOdemeleri\"", owner))
            Assert.Equal(1L, (long)(await sayCmd.ExecuteScalarAsync())!);

        foreach (var sql in new[] { "UPDATE \"MtvOdemeleri\" SET \"Tutar\" = 999", "DELETE FROM \"MtvOdemeleri\"" })
        {
            await using var cmd = new Npgsql.NpgsqlCommand(sql, owner);
            var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Contains("değişmezdir", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Kismi_odeme_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid mtv;
        using (var s1 = host.ScopeFor(t1))
        {
            var (sp, v) = await SeedAsync(s1, "34 TZ 01");
            var reg = sp.GetRequiredService<RegulationService>();
            mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
            await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = Guid.NewGuid() });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var reg2 = s2.ServiceProvider.GetRequiredService<RegulationService>();
        Assert.Empty(await reg2.ListMtvAsync());
        Assert.Empty(await reg2.ListMtvPaymentsAsync(mtv));
        await Assert.ThrowsAsync<ValidationException>(() => reg2.PayMtvAsync(mtv, LedgerAccountType.Kasa));

        // Kaynak tenant'ta bakiye bozulmadı.
        using var s1b = host.ScopeFor(t1);
        Assert.Equal(600m, (await s1b.ServiceProvider.GetRequiredService<RegulationService>()
            .ListMtvAsync()).Single(x => x.Id == mtv).Kalan);
    }
}
