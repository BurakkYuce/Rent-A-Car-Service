using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.GelenEFaturalar;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-55 — Gelen e-Fatura KDV oran kırılımı + araç/kategori/cari bağlama + GİDERLEŞTİRME (para).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> her beklenen değer testin İÇİNDE elle kurulmuş senaryodan gelir
/// (ör. "%20'lik 1000 → KDV 200; %10'luk 500 → KDV 50; toplam net 1500, KDV 250, genel 1750").
/// Hiçbir beklenti servis/rapor kodundan türetilmez.</para>
///
/// <para>Kapsam: kırılım doğrulaması (tutarlı/tutarsız), çok-oranlı belge kuruş-birebir, defter
/// dengesi + yön (indirilecek KDV Borç), idempotency (ikinci giderleştirme imkânsız), eşzamanlı
/// giderleştirme (TOCTOU), çift-sayım YOK regresyonu, çok-döviz, yetki, tenant izolasyonu,
/// giderleştirme sonrası kırılım kilidi.</para>
/// </summary>
[Collection("postgres")]
public sealed class GelenEFaturaKdvKirilimTests(PostgresFixture fx)
{
    // Belge tarihi: CI-vs-lokal tick farkı yüzünden tam SANİYEYE hizalı ve geçmişte.
    private static DateTimeOffset Day()
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-30), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static GelenEFaturaInput MakeInvoice(string ettn, decimal net, decimal vat, string currency = "TRY") => new()
    {
        Ettn = ettn,
        GonderenVkn = "1234567890",
        GonderenUnvan = "Tedarikçi A.Ş.",
        Tarih = Day(),
        NetTutar = net,
        KdvTutar = vat,
        GenelToplam = net + vat,
        Currency = currency
    };

    private static async Task<List<AccountLedgerEntry>> Ledger(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
    }

    // ---------------------------------------------------------------- (a) kırılım doğrulaması

    [Fact]
    public async Task Tutarli_kirilim_kaydedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // Senaryo (elle): %20 matrah 1000 → KDV 200. Belge: net 1000, KDV 200, genel 1200.
        var id = await svc.CreateManualAsync(MakeInvoice("KIR-1", 1000m, 200m));
        Assert.True(await svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = 1000m, Kdv20 = 200m
        }));

        var r = await svc.GetAsync(id);
        Assert.Equal(1000m, r!.Kdv20Matrah);
        Assert.Equal(200m, r.Kdv20);
        Assert.Null(r.Kdv10Matrah);
    }

    [Fact]
    public async Task Eksik_kirilim_toplami_tutmayinca_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // Belge net 1000 ama kırılımda yalnız 500 matrah girilmiş → Σ matrah ≠ NetTutar.
        var id = await svc.CreateManualAsync(MakeInvoice("KIR-2", 1000m, 200m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = 500m, Kdv20 = 100m
        }));

        // Reddedilen kırılım DB'ye SIZMAMALI (kısmen yazılmış alan kalmasın).
        var r = await svc.GetAsync(id);
        Assert.Null(r!.Kdv20Matrah);
        Assert.Null(r.Kdv20);
    }

    [Fact]
    public async Task Oranla_tutarsiz_kdv_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // Belge net 1000 / KDV 199: matrah toplamı tutuyor ama %20 için KDV 200 olmalıydı → red.
        var id = await svc.CreateManualAsync(MakeInvoice("KIR-3", 1000m, 199m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = 1000m, Kdv20 = 199m
        }));
    }

    [Fact]
    public async Task Belge_toplami_tutarsizsa_fatura_hic_olusmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // net 1000 + KDV 200 = 1200 ≠ genel toplam 1300 → belge kabul edilmez.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = "KIR-4", GonderenVkn = "1", GonderenUnvan = "X",
            NetTutar = 1000m, KdvTutar = 200m, GenelToplam = 1300m
        }));
        Assert.Empty(await svc.ListAsync());
    }

    // ---------------------------------------------------------------- (b) çok oranlı giderleştirme

    [Fact]
    public async Task Cok_oranli_fatura_oran_basina_gider_satiri_uretir_ve_kurus_birebir_tutar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var supplier = Guid.NewGuid();

        // ELLE KURULAN SENARYO (oracle):
        //   %20 → matrah 1000,00, KDV  200,00
        //   %10 → matrah  500,00, KDV   50,00
        //   %1  → matrah  300,00, KDV    3,00
        //   %0  → matrah  200,00, KDV    0,00
        //   Σ net = 2000,00 · Σ KDV = 253,00 · genel = 2253,00
        var id = await svc.CreateManualAsync(MakeInvoice("COK-1", 2000m, 253m));
        await svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id,
            Kdv20Matrah = 1000m, Kdv20 = 200m,
            Kdv10Matrah = 500m, Kdv10 = 50m,
            Kdv1Matrah = 300m, Kdv1 = 3m,
            Kdv0Matrah = 200m,
            CariId = supplier
        });
        Assert.True(await svc.ApproveAsync(id));

        var rowCount = await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput
        {
            Id = id, OdemeYontemi = PaymentMethod.AcikHesap, CariId = supplier
        });
        Assert.Equal(4, rowCount); // 4 oran kademesi = 4 gider satırı

        var expenseList = await expenses.ListAsync();
        Assert.Equal(4, expenseList.Count);
        Assert.Equal(2000m, expenseList.Sum(g => g.NetTutar));      // Σ matrah == belge neti
        Assert.Equal(253m, expenseList.Sum(g => g.KdvTutar));       // Σ KDV == belge KDV'si
        Assert.Equal(2253m, expenseList.Sum(g => g.GenelToplam));   // Σ brüt == belge genel toplamı

        // Defter: her satır için Borç Gider + Borç KDV (KDV>0 ise) + Alacak Cari.
        var ledger = await Ledger(scope);
        var debit = ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = ledger.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(2253m, debit);
        Assert.Equal(2253m, credit);
        Assert.Equal(debit, credit); // DENGELİ

        // İNDİRİLECEK KDV = BORÇ tarafında ve 253,00 (yön hatası çiti).
        var vatDebit = ledger.Where(e => e.AccountType == LedgerAccountType.Kdv && e.Direction == LedgerDirection.Debit)
            .Sum(e => e.Amount.AmountInBase);
        var vatCredit = ledger.Where(e => e.AccountType == LedgerAccountType.Kdv && e.Direction == LedgerDirection.Credit)
            .Sum(e => e.Amount.AmountInBase);
        Assert.Equal(253m, vatDebit);
        Assert.Equal(0m, vatCredit);
        Assert.Equal(3, ledger.Count(e => e.AccountType == LedgerAccountType.Kdv)); // %0 satırı KDV yazmaz

        // Gider hesabı 2000 borçlanır; tedarikçiye 2253 borçlanılır (bakiye negatif).
        Assert.Equal(2000m, ledger.Where(e => e.AccountType == LedgerAccountType.Gider
            && e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
        Assert.Equal(-2253m, await cash.GetAccountBalanceAsync(supplier));

        // Belge damgalandı.
        var r = await svc.GetAsync(id);
        Assert.NotNull(r!.GiderlestirilmeUtc);
        Assert.Equal(IncomingEInvoiceStatus.Islendi, r.Durum);
        Assert.Equal(id, r.GiderIslemAnahtari);
    }

    [Fact]
    public async Task Kirilim_yoksa_belge_toplamindan_tek_oran_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        // net 500 / KDV 50 → tek oran %10 (elle: 500 × 0,10 = 50).
        var id = await svc.CreateManualAsync(MakeInvoice("TEK-1", 500m, 50m));
        await svc.ApproveAsync(id);
        Assert.Equal(1, await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));

        var g = Assert.Single(await expenses.ListAsync());
        Assert.Equal(0.10m, g.KdvOrani);
        Assert.Equal(500m, g.NetTutar);
        Assert.Equal(50m, g.KdvTutar);
    }

    [Fact]
    public async Task Kirilimsiz_cozulemeyen_belge_gurultulu_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        // net 1000 / KDV 123 hiçbir standart orana (20/10/1/0) uymuyor → TAHMİN ETME, reddet.
        var id = await svc.CreateManualAsync(MakeInvoice("COZ-1", 1000m, 123m));
        await svc.ApproveAsync(id);
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));
        Assert.Empty(await expenses.ListAsync()); // defter/gider yok
    }

    // ---------------------------------------------------------------- idempotency + TOCTOU

    [Fact]
    public async Task Ikinci_giderlestirme_IMKANSIZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var id = await svc.CreateManualAsync(MakeInvoice("IDEM-1", 1000m, 200m));
        await svc.ApproveAsync(id);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });

        // F1.4: ikinci giderleştirme mükerrer gönderimdir (yarıştaki DB kısıtıyla aynı tip) → 409.
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));

        Assert.Single(await expenses.ListAsync());
        var ledger = await Ledger(scope);
        Assert.Equal(1200m, ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
    }

    [Fact]
    public async Task Damga_silinse_bile_DB_ikinci_defteri_reddeder()
    {
        // ADVERSARIAL: bellek-içi bayrağı (GiderlestirilmeUtc) elle temizleyip ikinci kez
        // giderleştirmeye çalış — gerçek çit DB'deki kısmi unique index (TenantId, IslemAnahtari)
        // olmalı. Bu, damganın yazılamadığı (çökme) senaryosunun da ampirik karşılığıdır.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var id = await svc.CreateManualAsync(MakeInvoice("IDEM-2", 1000m, 200m));
        await svc.ApproveAsync(id);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.GelenEFaturalar.FirstAsync(x => x.Id == id);
            row.GiderlestirilmeUtc = null;
            row.GiderIslemAnahtari = null;
            row.Durum = IncomingEInvoiceStatus.Onaylandi;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));

        Assert.Single(await expenses.ListAsync()); // hâlâ TEK gider
        var ledger = await Ledger(scope);
        Assert.Equal(1200m, ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
    }

    [Fact]
    public async Task Eszamanli_iki_giderlestirme_tek_defter_yazar()
    {
        // ADVERSARIAL (TOCTOU): iki paralel çağrı da "henüz giderleşmemiş" görüp devam ederse
        // ikincisi DB unique index'ine çarpmalı; defter TEK kez yazılmalı.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var id = await svc.CreateManualAsync(MakeInvoice("TOCTOU-1", 1000m, 200m));
        await svc.ApproveAsync(id);

        var t1 = svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });
        var t2 = svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });
        var results = await Task.WhenAll(
            Result(t1), Result(t2));
        Assert.Equal(1, results.Count(s => s)); // tam olarak biri başarılı

        Assert.Single(await expenses.ListAsync());
        var ledger = await Ledger(scope);
        Assert.Equal(1200m, ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
        Assert.Equal(1200m, ledger.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));

        static async Task<bool> Result(Task<int> t)
        {
            try { await t; return true; }
            catch (ValidationException) { return false; }
            catch (DbUpdateException) { return false; }
        }
    }

    // ---------------------------------------------------------------- ÇİFT SAYIM YOK (kırılgan regresyon)

    [Fact]
    public async Task Giderlestirilmemis_fatura_raporlara_SIZMAZ()
    {
        // KIRILGAN REGRESYON: gelen e-fatura hem kendi tablosunda hem gider olarak duracak.
        // Raporlar YALNIZ deftere bakmalı; bu tablodaki tutarlar hiçbir toplama girmemeli.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var once = await reports.GetRevenueExpenseAsync();
        Assert.Equal(0m, once.GiderToplam);
        Assert.Equal(0m, once.KdvIndirilecek);

        // UÇUK tutarlı bir gelen fatura: giderleştirilmediği sürece raporlar DEĞİŞMEMELİ.
        await svc.CreateManualAsync(MakeInvoice("SIZ-1", 999_999m, 199_999.80m));

        var after = await reports.GetRevenueExpenseAsync();
        Assert.Equal(0m, after.GiderToplam);
        Assert.Equal(0m, after.KdvIndirilecek);
        Assert.Equal(once.NetKar, after.NetKar);
    }

    [Fact]
    public async Task Giderlestirilen_fatura_raporlara_TAM_BIR_KEZ_girer()
    {
        // KIRILGAN REGRESYON: giderleştirmeden sonra rapor tam olarak belge kadar artmalı —
        // ne eksik (kayıp gider) ne fazla (çift sayım).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        // ELLE: %20 → 1000/200, %10 → 500/50. Σ net 1500, Σ KDV 250, genel 1750.
        var id = await svc.CreateManualAsync(MakeInvoice("SIZ-2", 1500m, 250m));
        await svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = 1000m, Kdv20 = 200m, Kdv10Matrah = 500m, Kdv10 = 50m
        });
        await svc.ApproveAsync(id);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });

        var gg = await reports.GetRevenueExpenseAsync();
        Assert.Equal(1500m, gg.GiderToplam);        // KDV gider değil (matrahlar)
        Assert.Equal(250m, gg.KdvIndirilecek);      // indirilecek KDV ayrı
        Assert.Equal(0m, gg.KdvTahsil);
        Assert.Equal(-1500m, gg.NetKar);

        // Kaynak kırılımı tek "Gider" kaleminde toplanır — gelen fatura tablosu ayrı bir kalem
        // olarak GÖRÜNMEZ (görünseydi toplam iki kez sayılıyor demekti).
        Assert.Equal(1500m, Assert.Single(gg.GiderKirilim, k => k.SourceType == "Gider").Tutar);
        Assert.Single(gg.GiderKirilim);
    }

    // ---------------------------------------------------------------- çok döviz

    [Fact]
    public async Task Dovizli_fatura_kur_ile_yansir_ve_dengeli_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // ELLE: 1 EUR = 40 TRY (test kuru, belge tarihinden önce yayımlanmış).
        // %20 → matrah 100 EUR, KDV 20 EUR, brüt 120 EUR.
        var factory0 = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var seed = await factory0.CreateDbContextAsync())
        {
            seed.KurKayitlari.Add(new KurKaydi
            {
                Kod = "EUR", Ad = "EUR", Birim = 1,
                Tarih = Day().AddDays(-1),
                ForexSatis = 40m, ForexAlis = 40m, EfektifSatis = 40m, EfektifAlis = 40m
            });
            await seed.SaveChangesAsync();
        }

        var id = await svc.CreateManualAsync(MakeInvoice("DVZ-1", 100m, 20m, "EUR"));
        await svc.ApproveAsync(id);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });

        var ledger = await Ledger(scope);
        Assert.All(ledger, e => Assert.Equal("EUR", e.Amount.Currency));
        Assert.All(ledger, e => Assert.Equal(40m, e.Amount.Rate));
        var debit = ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = ledger.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(4800m, debit);   // 120 EUR × 40 (elle: 100×40 gider + 20×40 KDV)
        Assert.Equal(4800m, credit); // 120 EUR × 40 kasa
    }

    // ---------------------------------------------------------------- kapılar, kilit, yetki, izolasyon

    [Fact]
    public async Task Yalniz_onaylanmis_fatura_giderlestirilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var pending = await svc.CreateManualAsync(MakeInvoice("KAPI-1", 100m, 20m));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = pending, OdemeYontemi = PaymentMethod.Nakit }));

        var rejected = await svc.CreateManualAsync(MakeInvoice("KAPI-2", 100m, 20m));
        await svc.RejectAsync(rejected, "mükerrer");
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = rejected, OdemeYontemi = PaymentMethod.Nakit }));

        // "Defter dışı işle" ile İşlendi'ye alınmış fatura da giderleştirilemez (çift kayıt çiti).
        var manual = await svc.CreateManualAsync(MakeInvoice("KAPI-3", 100m, 20m));
        await svc.ApproveAsync(manual);
        await svc.IsleAsync(manual);
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = manual, OdemeYontemi = PaymentMethod.Nakit }));

        Assert.Empty(await expenses.ListAsync());
    }

    [Fact]
    public async Task Giderlestirilmis_faturanin_kirilimi_KILITLI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        var id = await svc.CreateManualAsync(MakeInvoice("KILIT-1", 1000m, 200m));
        await svc.LinkAsync(new GelenEFaturaBaglamaInput { Id = id, Kdv20Matrah = 1000m, Kdv20 = 200m });
        await svc.ApproveAsync(id);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });

        // Defter yazıldıktan sonra belgeyi değiştirmek defterle diverge üretirdi → red.
        await Assert.ThrowsAsync<ValidationException>(() => svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv10Matrah = 1000m, Kdv10 = 100m
        }));
        var r = await svc.GetAsync(id);
        Assert.Equal(1000m, r!.Kdv20Matrah); // eski değer duruyor
        Assert.Null(r.Kdv10Matrah);
    }

    [Fact]
    public async Task Acik_hesapta_cari_zorunlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        var id = await svc.CreateManualAsync(MakeInvoice("CARI-1", 100m, 20m));
        await svc.ApproveAsync(id);
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.AcikHesap }));
    }

    [Fact]
    public async Task Arac_bagi_gider_satirlarina_tasinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var vehicle = Guid.NewGuid();

        var id = await svc.CreateManualAsync(MakeInvoice("ARAC-1", 1000m, 200m));
        await svc.LinkAsync(new GelenEFaturaBaglamaInput { Id = id, VehicleId = vehicle });
        await svc.ApproveAsync(id);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });

        var g = Assert.Single(await expenses.ListAsync());
        Assert.Equal(ExpenseType.Arac, g.Tip);   // araç bağlıysa tür otomatik Araç
        Assert.Equal(vehicle, g.VehicleId);

        // Karne/karlılık atfı: Gider defter satırının AccountRef'i araçtır.
        var ledger = await Ledger(scope);
        Assert.Equal(vehicle, ledger.Single(e => e.AccountType == LedgerAccountType.Gider).AccountRef);
    }

    [Fact]
    public async Task NonFinance_kullanici_baglayamaz_ve_giderlestiremez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id;
        using (var admin = host.ScopeFor(tenant))
        {
            var s = admin.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
            id = await s.CreateManualAsync(MakeInvoice("YETKI-1", 100m, 20m));
            await s.ApproveAsync(id);
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        var svc = op.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            svc.LinkAsync(new GelenEFaturaBaglamaInput { Id = id, Kdv20Matrah = 100m, Kdv20 = 20m }));
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));
    }

    [Fact]
    public async Task Baska_tenant_faturasi_giderlestirilemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
        {
            var s = s1.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
            id = await s.CreateManualAsync(MakeInvoice("IZO-1", 1000m, 200m));
            await s.ApproveAsync(id);
        }

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        // RLS + query filter: t2 için satır YOK → "bulunamadı" (sızıntı yok, defter yazılmaz).
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc2.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ExpenseService>().ListAsync());
    }

    // ---------------------------------------------------------------- adversarial regresyonlar

    [Fact]
    public async Task Kurus_alti_tutar_reddedilir()
    {
        // ADVERSARIAL: kolonlar numeric(19,4) → 1000,0050 SAKLANABİLİR. Kırılım/gider yolu 2
        // ondalığa yuvarladığından defter 1000,01 taşır, belge 1000,0050 gösterirdi (kuruş-altı
        // kopma). Girişte reddedilmeli.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateManualAsync(MakeInvoice("KRS-1", 1000.0050m, 200.0010m)));

        // Kırılım kolonlarında da aynı çit geçerli.
        var id = await svc.CreateManualAsync(MakeInvoice("KRS-2", 1000m, 200m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = 999.9950m, Kdv20 = 200m, Kdv0Matrah = 0.0050m
        }));
        Assert.Null((await svc.GetAsync(id))!.Kdv20Matrah);
    }

    [Fact]
    public async Task Matrahsiz_kdv_kademesi_reddedilir()
    {
        // ADVERSARIAL: yalnız KDV kolonu doldurularak "bedava indirilecek KDV" üretilebilir mi?
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        var id = await svc.CreateManualAsync(MakeInvoice("MTR-1", 1000m, 200m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.LinkAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = 1000m, Kdv20 = 200m, Kdv10 = 500m // matrahsız 500 KDV
        }));
    }

    [Fact]
    public async Task Kapali_donemde_giderlestirme_defter_yazmaz()
    {
        // ADVERSARIAL: dönem kilidi gider yolunun sorumluluğunda — gelen fatura onu ATLAYAMAMALI.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var id = await svc.CreateManualAsync(MakeInvoice("KLT-1", 1000m, 200m));
        await svc.ApproveAsync(id);
        await scope.ServiceProvider.GetRequiredService<RentACar.Application.Periods.PeriodLockService>()
            .LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));

        Assert.Empty(await expenses.ListAsync());
        Assert.Empty(await Ledger(scope));
        // Belge damgalanmamış olmalı → kilit açılınca tekrar denenebilir.
        Assert.Null((await svc.GetAsync(id))!.GiderlestirilmeUtc);
    }

    [Fact]
    public async Task Tanimsiz_dovizde_gurultulu_red_defter_yazmaz()
    {
        // ADVERSARIAL: kuru bilinmeyen dövizde sessizce kur=1 kullanılırsa TL maliyeti uydurulmuş olur.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var id = await svc.CreateManualAsync(MakeInvoice("DVZ-2", 100m, 20m, "XAU"));
        await svc.ApproveAsync(id);
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));
        Assert.Empty(await expenses.ListAsync());
        Assert.Empty(await Ledger(scope));
    }

    // ---------------------------------------------------------------- filtreler

    [Fact]
    public async Task Filtreler_firma_ettn_araligi_durum_ve_defter_durumu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        await svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = "FLT-100", GonderenVkn = "1111111111", GonderenUnvan = "Alfa Lojistik",
            Tarih = Day(), NetTutar = 100m, KdvTutar = 20m, GenelToplam = 120m
        });
        await svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = "FLT-200", GonderenVkn = "2222222222", GonderenUnvan = "Beta Servis",
            Tarih = Day(), NetTutar = 200m, KdvTutar = 40m, GenelToplam = 240m
        });
        var third = await svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = "FLT-300", GonderenVkn = "3333333333", GonderenUnvan = "Gama Petrol",
            Tarih = Day(), NetTutar = 300m, KdvTutar = 60m, GenelToplam = 360m
        });
        await svc.ApproveAsync(third);
        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = third, OdemeYontemi = PaymentMethod.Nakit });

        Assert.Single(await svc.ListAsync(new GelenEFaturaFilter { Firma = "beta" }));         // ünvan (ILike)
        Assert.Single(await svc.ListAsync(new GelenEFaturaFilter { Firma = "3333333333" }));   // VKN
        Assert.Equal(2, (await svc.ListAsync(new GelenEFaturaFilter { EttnBas = "FLT-200" })).Count);
        Assert.Equal(2, (await svc.ListAsync(new GelenEFaturaFilter { EttnBas = "FLT-100", EttnBit = "FLT-200" })).Count);
        Assert.Equal(2, (await svc.ListAsync(new GelenEFaturaFilter { Durum = IncomingEInvoiceStatus.Beklemede })).Count);
        Assert.Single(await svc.ListAsync(new GelenEFaturaFilter { Giderlestirildi = true }));
        Assert.Equal(2, (await svc.ListAsync(new GelenEFaturaFilter { Giderlestirildi = false })).Count);
    }
}
