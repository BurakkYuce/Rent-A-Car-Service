using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// ADVERSARIAL probe'ları: dönem-sonu kapanış fişini (PR-A close-lite) canlı PG'ye karşı ÇÜRÜTME denemesi.
/// 5 saldırı vektörü: (1) çok-döviz base-TL sıfırlama, (2) işaret kenarları, (3) eşzamanlı yarış,
/// (4) P&L sızıntısı, (5) kilit/yeniden-kapatma. Beklenen değerler ELLE kurulmuş senaryodan (bağımsız oracle).
/// </summary>
[Collection("postgres")]
public sealed class DonemKapanisFisiAdversarialProbeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset IsTarih = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Kapanis = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

    private static IDbContextFactory<AppDbContext> Factory(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    private static decimal Bakiye(IReadOnlyList<MizanSatirDto> mizan, LedgerAccountType t)
        => mizan.FirstOrDefault(m => m.Tip == t)?.Bakiye ?? 0m;

    /// <summary>Balanced çift yaz (INSERT serbest; immutability yalnız update/delete engeller):
    /// Gelir/Gider karşısına Kasa yazar → defter dengede kalır, kapanışa dövizli veri hazırlanır.</summary>
    private static async Task PostBalancedAsync(IServiceScope scope, LedgerAccountType pnlTip,
        LedgerDirection pnlYon, decimal amount, string ccy, decimal rate, DateTimeOffset tarih, string src = "Manuel")
    {
        await using var db = await Factory(scope).CreateDbContextAsync();
        var karsiYon = pnlYon == LedgerDirection.Credit ? LedgerDirection.Debit : LedgerDirection.Credit;
        var sid = Guid.NewGuid();
        db.AccountLedgerEntries.Add(new AccountLedgerEntry
        {
            AccountType = pnlTip, Direction = pnlYon, AccountRef = null,
            Amount = new Money(amount, ccy, rate), EntryDateUtc = tarih, SourceType = src, SourceId = sid
        });
        db.AccountLedgerEntries.Add(new AccountLedgerEntry
        {
            AccountType = LedgerAccountType.Kasa, Direction = karsiYon, AccountRef = null,
            Amount = new Money(amount, ccy, rate), EntryDateUtc = tarih, SourceType = src, SourceId = sid
        });
        await db.SaveChangesAsync();
    }

    // ============ VEKTÖR 1: ÇOK-DÖVİZ ============

    /// <summary>V1-a (TEMİZ kur): EUR 1000 @ 30 = base 30000 (4dp'ye tam sığar). Kapanış base-TL'yi TAM sıfırlamalı,
    /// DonemSonucu = −30000 (kâr). Oracle: 1000×30 = 30000 elle.</summary>
    [Fact]
    public async Task Probe_V1a_dovizli_gelir_temiz_kur_base_TL_tam_sifirlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await PostBalancedAsync(scope, LedgerAccountType.Gelir, LedgerDirection.Credit, 1000m, "EUR", 30m, IsTarih);

        await scope.ServiceProvider.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var mizan = await scope.ServiceProvider.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));          // base-TL TAM sıfır
        Assert.Equal(-30000m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // net base-TL
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));                        // denge
    }

    /// <summary>V1-b (KİRLİ kur — YUVARLAMA): Amount 123.4567 @ Rate 1.000001 → base = 123.4568234567 (10dp).
    /// Kapanış fişi Amount'u numeric(19,4)'e YUVARLAR → Gelir'de KURUŞ-ALTI (&lt;0.0001 TRY) artık kalır. Bu
    /// artık defterin kendi 4-hane saklama hassasiyetinin ALTINDA, BİRİKMEZ (her kapanış güncel bakiyeyi okur)
    /// ve defter GENEL dengesi korunur. INVARIANT: |artık| &lt; 0.0001 ve Σ bakiye = 0 (adversarial BULGU 1, düşük).</summary>
    [Fact]
    public async Task Probe_V1b_dovizli_yuvarlama_kurus_alti_artik_denge_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // base = 123.4567 × 1.000001 = 123.4568234567 → 4dp'ye sığmaz.
        await PostBalancedAsync(scope, LedgerAccountType.Gelir, LedgerDirection.Credit, 123.4567m, "EUR", 1.000001m, IsTarih);

        var rapor = scope.ServiceProvider.GetRequiredService<ReportService>();
        await scope.ServiceProvider.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var mizan = await rapor.GetMizanAsync();
        var gelirKalan = Bakiye(mizan, LedgerAccountType.Gelir);

        Assert.True(Math.Abs(gelirKalan) < 0.0001m, $"Gelir artığı kuruş-altı olmalı, bulunan {gelirKalan}");
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye)); // defter GENEL dengesi her hâlükârda korunur
    }

    // ============ VEKTÖR 2: İŞARET KENARLARI ============

    [Fact]
    public async Task Probe_V2_yalniz_gelir_gider_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = await Cari(sp), NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });

        await sp.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var mizan = await sp.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));
        Assert.Equal(-1000m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // yalnız kâr
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));
    }

    [Fact]
    public async Task Probe_V2_yalniz_gider_gelir_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 400m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Tarih = IsTarih });

        await sp.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var mizan = await sp.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gider));
        Assert.Equal(400m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // zarar → Borç bakiye +
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));
    }

    /// <summary>Net TAM 0 (gelir==gider): DonemSonucu entry ÜRETİLMEMELİ, Gelir/Gider sıfırlanmalı, denge korunmalı.</summary>
    [Fact]
    public async Task Probe_V2_net_sifir_DonemSonucu_entry_uretmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = cari, NetTutar = 500m, KdvOrani = 0m, Tarih = IsTarih });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 500m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Tarih = IsTarih });

        await sp.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var mizan = await sp.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gider));
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.DonemSonucu));
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));

        await using var db = await Factory(scope).CreateDbContextAsync();
        var dsEntry = await db.AccountLedgerEntries.AsNoTracking()
            .CountAsync(e => e.SourceType == "DonemKapanis" && e.AccountType == LedgerAccountType.DonemSonucu);
        Assert.Equal(0, dsEntry); // net 0 → 0-tutarlı DonemSonucu entry YAZILMAMALI
    }

    // ============ VEKTÖR 3: EŞZAMANLI YARIŞ ============

    /// <summary>V3-a: AYNI tarih iki eşzamanlı KapatAsync → advisory-lock serialization çift fişi engellemeli
    /// (ikinci güncel bakiyeyi=0 okur → boş fiş). DonemSonucu tek (−700), Gelir sıfır, TEK DonemSonucu entry.</summary>
    [Fact]
    public async Task Probe_V3a_eszamanli_ayni_tarih_cift_fis_yok()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var cari = await Cari(sp);
            await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
            { CariId = cari, NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });
            await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
            { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Tarih = IsTarih });
        }

        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        var exceptions = new List<Exception>();
        async Task Kapat(IServiceScope s)
        {
            try { await s.ServiceProvider.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis); }
            catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
        }
        await Task.WhenAll(Kapat(s1), Kapat(s2));

        using var verify = host.ScopeFor(tenant);
        var mizan = await verify.ServiceProvider.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));
        Assert.Equal(-700m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // −1400 DEĞİL
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));

        await using var db = await Factory(verify).CreateDbContextAsync();
        var dsCount = await db.AccountLedgerEntries.AsNoTracking()
            .CountAsync(e => e.SourceType == "DonemKapanis" && e.AccountType == LedgerAccountType.DonemSonucu);
        Assert.Equal(1, dsCount); // TEK kapanış fişi
    }

    /// <summary>V3-b: FARKLI tarih iki eşzamanlı KapatAsync (June29 + June30). SourceId farklı → unique index dedup ETMEZ.
    /// Eşzamanlı okuma stale balance yakalarsa ÇİFT-SAYIM olur. Sonucu ölç (kilit last-writer-wins).</summary>
    [Fact]
    public async Task Probe_V3b_eszamanli_farkli_tarih_cift_sayim_olur_mu()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var seed = host.ScopeFor(tenant))
        {
            await seed.ServiceProvider.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
            { CariId = await Cari(seed.ServiceProvider), NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });
        }
        var haziran29 = new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);

        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        var errs = new List<Exception>();
        async Task Kapat(IServiceScope s, DateTimeOffset d)
        {
            try { await s.ServiceProvider.GetRequiredService<DonemKapanisFisiService>().KapatAsync(d); }
            catch (Exception ex) { lock (errs) errs.Add(ex); }
        }
        await Task.WhenAll(Kapat(s1, haziran29), Kapat(s2, Kapanis));

        using var verify = host.ScopeFor(tenant);
        var mizan = await verify.ServiceProvider.GetRequiredService<ReportService>().GetMizanAsync();
        var gelir = Bakiye(mizan, LedgerAccountType.Gelir);
        var ds = Bakiye(mizan, LedgerAccountType.DonemSonucu);
        await using var db = await Factory(verify).CreateDbContextAsync();
        var dsCount = await db.AccountLedgerEntries.AsNoTracking()
            .CountAsync(e => e.SourceType == "DonemKapanis" && e.AccountType == LedgerAccountType.DonemSonucu);
        Console.WriteLine($"[V3b] Gelir kalan = {gelir} (dogru: 0), DonemSonucu = {ds} (dogru: -1000), DonemSonucu entry sayisi = {dsCount} (dogru: 1)");
        Console.WriteLine($"[V3b] hatalar = {errs.Count}");

        // DOĞRU sonuç: gelir 1000 yalnız BİR kez kapanmalı → Gelir 0, DonemSonucu −1000, denge korunur.
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye)); // denge her hâlükârda korunmalı
        Assert.Equal(0m, gelir);      // çift-kapanış → Gelir negatife düşer (bu assert çürütmeyi yakalar)
        Assert.Equal(-1000m, ds);     // çift-kapanış → −2000
    }

    // ============ VEKTÖR 4: P&L SIZINTISI ============

    /// <summary>Kapanış fişi HİÇBİR gelir-gider raporuna sızmamalı: GelirGider (toplam + kırılım), Karlılık,
    /// Karlılık-özet (grup/şube/segment), aylık trend. Kira-faturalı gelir (araç-atıflı) → Karlılık araç satırı değişmez.</summary>
    [Fact]
    public async Task Probe_V4_kapanis_fisi_hicbir_PnL_raporuna_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = cari, NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Tarih = IsTarih });
        var rapor = sp.GetRequiredService<ReportService>();

        var ggOnce = await rapor.GetGelirGiderAsync();
        var karOnce = await rapor.GetKarlilikAsync();
        // Ozet unattributed (VehicleId=null) geliri KAPSAMAZ (tasarım) → 0; leak testi ONCE==SONRA ile yapılır.
        var ozetOnce = new Dictionary<string, decimal>();
        foreach (var b in new[] { "grup", "sube", "segment" })
            ozetOnce[b] = (await rapor.GetKarlilikOzetAsync(b)).ToplamNetKar;

        await sp.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var ggSonra = await rapor.GetGelirGiderAsync();
        Assert.Equal(ggOnce.GelirToplam, ggSonra.GelirToplam);
        Assert.Equal(ggOnce.GiderToplam, ggSonra.GiderToplam);
        Assert.Equal(1000m, ggSonra.GelirToplam);
        Assert.Equal(300m, ggSonra.GiderToplam);
        // Kırılım listelerinde 'DonemKapanis' kalemi OLMAMALI.
        Assert.DoesNotContain(ggSonra.GelirKirilim, k => k.SourceType == "DonemKapanis");
        Assert.DoesNotContain(ggSonra.GiderKirilim, k => k.SourceType == "DonemKapanis");

        // Karlılık toplamı + araç/(Atanmamış) satırları değişmemeli.
        var karSonra = await rapor.GetKarlilikAsync();
        Assert.Equal(karOnce.ToplamNetKar, karSonra.ToplamNetKar);
        Assert.Equal(700m, karSonra.ToplamNetKar);
        Assert.Equal(1000m, karSonra.ToplamGelir);
        Assert.Equal(300m, karSonra.ToplamGider);

        // Karlılık-özet (üç boyut): kapanış öncesi==sonrası (kapanış fişi sızmıyor).
        foreach (var boyut in new[] { "grup", "sube", "segment" })
        {
            var ozet = await rapor.GetKarlilikOzetAsync(boyut);
            Assert.Equal(ozetOnce[boyut], ozet.ToplamNetKar);
        }

        // Aylık trend: Haziran ayında gelir 1000 / gider 300 — kapanış etkilemez.
        var trend = await rapor.GetAylikGelirGiderTrendAsync(1, new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(1000m, trend[0].Gelir);
        Assert.Equal(300m, trend[0].Gider);
    }

    // ============ VEKTÖR 5: KİLİT / YENİDEN-KAPATMA ============

    /// <summary>V5-a (BULGU 2 düzeltmesi): kapat → aç → GEÇMİŞE (dönem içine) yeni gelir → AYNI tarihi tekrar kapat.
    /// TAZE SourceId + serialization ile yeniden-kapatma güncel bakiyeyi (yeni 500) DOĞRU yakalar → Gelir 0,
    /// DonemSonucu −1500 (sessiz no-op YOK). Düzeltmeden önce deterministik SourceId çakışması yeni geliri yutuyordu.</summary>
    [Fact]
    public async Task Probe_V5a_reopen_ayni_tarih_yeni_kayit_yakalanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var inv = sp.GetRequiredService<InvoiceService>();
        var kapanis = sp.GetRequiredService<DonemKapanisFisiService>();
        var donem = sp.GetRequiredService<DonemKilidiService>();
        var rapor = sp.GetRequiredService<ReportService>();

        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });
        await kapanis.KapatAsync(Kapanis);
        Assert.Equal(-1000m, Bakiye(await rapor.GetMizanAsync(), LedgerAccountType.DonemSonucu));

        // Kilidi aç, dönem İÇİNE yeni gelir 500 ekle (geçmişe düzeltme senaryosu).
        await donem.UnlockAsync();
        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 500m, KdvOrani = 0m, Tarih = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero) });

        // AYNI tarihi tekrar kapat — HATA beklemeyiz (mevcut kilit yok, guard geçer).
        await kapanis.KapatAsync(Kapanis);

        var mizan = await rapor.GetMizanAsync();
        var gelir = Bakiye(mizan, LedgerAccountType.Gelir);
        var ds = Bakiye(mizan, LedgerAccountType.DonemSonucu);
        var kilit = await donem.GetClosingDateAsync();
        Console.WriteLine($"[V5a] tekrar-kapatma SONRASI: Gelir = {gelir} (dogru: 0), DonemSonucu = {ds} (dogru: -1500), kilit = {kilit:yyyy-MM-dd}");

        Assert.NotNull(kilit); // dönem yine kilitli (KapatAsync 'başarılı' döndü)
        // DOĞRU davranış: yeni 500 de kapanmalı → Gelir 0, DonemSonucu −1500.
        Assert.Equal(0m, gelir);
        Assert.Equal(-1500m, ds);
    }

    /// <summary>V5-b (ÖNERİLEN İŞ AKIŞI — kontrol): kapat → aç → dönem içine yeni gelir → İLERİ tarihe kapat.
    /// İleri tarih farklı SourceId → yeni delta doğru yakalanır (V5-a'nın sessiz no-op'una karşı doğru yol).</summary>
    [Fact]
    public async Task Probe_V5b_reopen_ileri_tarih_yeni_kayit_dogru_yakalanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await Cari(sp);
        var inv = sp.GetRequiredService<InvoiceService>();
        var kapanis = sp.GetRequiredService<DonemKapanisFisiService>();
        var donem = sp.GetRequiredService<DonemKilidiService>();
        var rapor = sp.GetRequiredService<ReportService>();

        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });
        await kapanis.KapatAsync(Kapanis);
        await donem.UnlockAsync();
        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 500m, KdvOrani = 0m, Tarih = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero) });

        // İLERİ tarihe kapat (temmuz 1) → yeni delta yakalanmalı.
        await kapanis.KapatAsync(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var mizan = await rapor.GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));           // ileri tarih delta'yı toplar
        Assert.Equal(-1500m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // 1000 + 500
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));
    }

    /// <summary>V3-c (REGRESYON KİLİDİ — V3-b'nin yakaladığı GERÇEK para hatasının düzeltmesi):
    /// GEÇ tarihli kapanış ÖNCE commit edip ARDINDAN erken tarihli kapanış gelirse, erken kapanış
    /// bakiyeyi kendi (daha erken) kesim anına göre okuduğu için geç kapanışın fişini GÖREMEZ ve aynı
    /// geliri İKİNCİ kez kapatırdı (Gelir +1000, DonemSonucu −2000, iki DonemSonucu fişi). Bu senaryo
    /// V3-b'de eşzamanlılık sırasına bağlı olarak ~%10-20 tekrar ediyordu ("flaky" görünen gerçek bug).
    /// Düzeltme: "zaten kapalı" guard'ı advisory KİLİDİN İÇİNDE (repo). Burada YARIŞ YOK — sıralı çağrı
    /// ile deterministik kanıt.</summary>
    [Fact]
    public async Task Probe_V3c_gec_tarihten_sonra_erken_tarihe_kapatma_reddedilir_cift_saymaz()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var seed = host.ScopeFor(tenant))
        {
            await seed.ServiceProvider.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
            { CariId = await Cari(seed.ServiceProvider), NetTutar = 1000m, KdvOrani = 0m, Tarih = IsTarih });
        }

        using var scope = host.ScopeFor(tenant);
        // Servis ön-kontrolünü (kilidin DIŞINDA) BİLEREK atlayıp doğrudan repo'yu çağırıyoruz — eşzamanlı
        // yarışta iki çağrının da ön-kontrolden geçtiği durumun birebir aynısı.
        var repo = scope.ServiceProvider.GetRequiredService<IDonemKapanisRepository>();
        await repo.KapatAsync(Kapanis); // önce GEÇ tarih (30 Haziran)

        await Assert.ThrowsAsync<ValidationException>(
            () => repo.KapatAsync(new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero))); // sonra ERKEN tarih → RED

        var mizan = await scope.ServiceProvider.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));           // ÇİFT SAYIM YOK (bug'da +1000 idi)
        Assert.Equal(-1000m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // bug'da −2000 idi
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));

        await using var db = await Factory(scope).CreateDbContextAsync();
        var dsCount = await db.AccountLedgerEntries.AsNoTracking()
            .CountAsync(e => e.SourceType == "DonemKapanis" && e.AccountType == LedgerAccountType.DonemSonucu);
        Assert.Equal(1, dsCount); // bug'da 2 idi
    }

    private static Task<Guid> Cari(IServiceProvider sp)
        => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Probe" });
}
