using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-56 — manuel bakiye düzeltmesi (Kasa/Banka'ya DOKUNMADAN cari bakiyesi ayarlama).
///
/// <para><b>Karar (KARARLAR.md FAZ-56):</b> dengeli çiftin karşı bacağı yeni
/// <see cref="LedgerAccountType.MuhasebeDuzeltmesi"/> türüdür. Gelir/Gider'e yazmak Araç Karnesi,
/// Kârlılık ve Filo Analiz raporlarını şişirirdi — CLAUDE.md §6'daki atıf düzeltmesi tam bu sınıf
/// bir hatayı bir kez zaten temizledi. Buradaki "P&amp;L kirlenmiyor" testleri o kararı kalıcı kılar.</para>
///
/// <para><b>Bağımsız oracle:</b> beklenen bakiyeler elle kurulan senaryodan (1000 borç − 150
/// alacaklandırma = 850), servis kodundan DEĞİL.</para>
/// </summary>
[Collection("postgres")]
public sealed class BakiyeDuzeltmeTests(PostgresFixture fx)
{
    private static DateTimeOffset Gun(int fark)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fark), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> CariAsync(IServiceScope s, string ad)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test" });

    /// <summary>Cariye 1000 TL borç yükler (ödeme: Borç Cari / Alacak Kasa).</summary>
    private static async Task Borclandır(IServiceScope s, Guid cari, decimal tutar = 1000m)
    {
        var kasa = await s.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = $"K{Guid.NewGuid():N}"[..8], Ad = "Kasa", Tur = "Kasa" });
        await s.ServiceProvider.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = cari, Tutar = tutar, Hesap = LedgerAccountType.Kasa, HesapId = kasa });
    }

    private static async Task<List<(LedgerAccountType Tur, LedgerDirection Yon, decimal Baz)>>
        DuzeltmeSatirlariAsync(TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == BakiyeDuzeltmeService.Kaynak)
            .Select(e => new { e.AccountType, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync();
        return [.. rows.Select(r => (r.AccountType, r.Direction, r.A * r.R))];
    }

    // ---------------------------------------------------------------- Yön + denge

    [Fact]
    public async Task Alacaklandirma_bakiyeyi_DUSURUR_ve_dengeli_yazar()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "Alacak");
        await Borclandır(scope, cari);   // 1000 borç

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 150m, Yon = BakiyeDuzeltmeYonu.Alacaklandir });

        // Elle: 1000 − 150 = 850.
        Assert.Equal(850m, await cash.GetCariBalanceAsync(cari));

        var satirlar = await DuzeltmeSatirlariAsync(host, tenant);
        Assert.Equal(2, satirlar.Count);
        // Cari ALACAK (bakiye düşer) / karşı bacak Muhasebe Düzeltmesi BORÇ.
        Assert.Contains(satirlar, x => x.Tur == LedgerAccountType.Cari
            && x.Yon == LedgerDirection.Credit && x.Baz == 150m);
        Assert.Contains(satirlar, x => x.Tur == LedgerAccountType.MuhasebeDuzeltmesi
            && x.Yon == LedgerDirection.Debit && x.Baz == 150m);
        // Denge.
        Assert.Equal(
            satirlar.Where(x => x.Yon == LedgerDirection.Debit).Sum(x => x.Baz),
            satirlar.Where(x => x.Yon == LedgerDirection.Credit).Sum(x => x.Baz));
    }

    [Fact]
    public async Task Borclandirma_bakiyeyi_YUKSELTIR()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "Borc");
        await Borclandır(scope, cari);   // 1000

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 250m, Yon = BakiyeDuzeltmeYonu.Borclandir });

        // Elle: 1000 + 250 = 1250.
        Assert.Equal(1250m, await cash.GetCariBalanceAsync(cari));
        var satirlar = await DuzeltmeSatirlariAsync(host, tenant);
        Assert.Contains(satirlar, x => x.Tur == LedgerAccountType.Cari && x.Yon == LedgerDirection.Debit);
        Assert.Contains(satirlar, x => x.Tur == LedgerAccountType.MuhasebeDuzeltmesi && x.Yon == LedgerDirection.Credit);
    }

    [Fact]
    public async Task Ters_yonlu_ikinci_duzeltme_ilkini_kapatir()
    {
        // Defter değişmezdir: yanlış düzeltme SİLİNMEZ, ters kayıtla kapatılır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "TersKayit");
        await Borclandır(scope, cari);

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 300m, Yon = BakiyeDuzeltmeYonu.Alacaklandir });
        Assert.Equal(700m, await cash.GetCariBalanceAsync(cari));

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 300m, Yon = BakiyeDuzeltmeYonu.Borclandir });
        Assert.Equal(1000m, await cash.GetCariBalanceAsync(cari));
    }

    // ---------------------------------------------------------------- P&L kirlenmiyor (kararın özü)

    [Fact]
    public async Task Duzeltme_GELIR_GIDER_ve_KARLILIK_toplamlarina_KARISMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await CariAsync(scope, "PnL");
        await Borclandır(scope, cari);

        var ggOnce = await reports.GetGelirGiderAsync();
        var karneOnce = await reports.GetFiloAnalizAsync();

        // UÇUK bir düzeltme: Gelir/Gider'e yazılsaydı raporlar şişerdi.
        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 999_999m, Yon = BakiyeDuzeltmeYonu.Alacaklandir });

        var ggSonra = await reports.GetGelirGiderAsync();
        Assert.Equal(ggOnce.GelirToplam, ggSonra.GelirToplam);
        Assert.Equal(ggOnce.GiderToplam, ggSonra.GiderToplam);
        Assert.Equal(ggOnce.NetKar, ggSonra.NetKar);

        var karneSonra = await reports.GetFiloAnalizAsync();
        Assert.Equal(karneOnce.Satirlar.Sum(x => x.Gelir), karneSonra.Satirlar.Sum(x => x.Gelir));
        Assert.Equal(karneOnce.Satirlar.Sum(x => x.Gider), karneSonra.Satirlar.Sum(x => x.Gider));

        // …ama cari bakiyesi GERÇEKTEN değişti (düzeltme işini yaptı).
        Assert.Equal(1000m - 999_999m,
            await scope.ServiceProvider.GetRequiredService<CashService>().GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Duzeltme_KASA_BANKA_bakiyesine_DOKUNMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await CariAsync(scope, "Kasa");
        await Borclandır(scope, cari);   // kasa −1000

        var once = await reports.GetKasaBankaSummaryAsync();
        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 400m, Yon = BakiyeDuzeltmeYonu.Alacaklandir });
        var sonra = await reports.GetKasaBankaSummaryAsync();

        Assert.Equal(once.KasaBakiye, sonra.KasaBakiye);
        Assert.Equal(once.BankaBakiye, sonra.BankaBakiye);
        Assert.Equal(-1000m, sonra.KasaBakiye);   // elle: yalnız ödeme etkisi
    }

    // ---------------------------------------------------------------- Adversarial kilitler

    [Fact]
    public async Task Cift_submit_yutulur_bakiye_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "CiftSubmit");
        await Borclandır(scope, cari);

        var anahtar = Guid.NewGuid();
        var girdi = () => new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 200m, Yon = BakiyeDuzeltmeYonu.Alacaklandir, IslemAnahtari = anahtar };
        await svc.AdjustAsync(girdi());
        await svc.AdjustAsync(girdi());   // aynı token → yutulur

        // Elle: 1000 − 200 = 800 (600 DEĞİL).
        Assert.Equal(800m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Gecersiz_girdiler_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cari = await CariAsync(scope, "Gecersiz");

        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = cari, Tutar = 0m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = cari, Tutar = -50m }));
        // Uydurma cari.
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = Guid.NewGuid(), Tutar = 100m }));
        // Gelecek tarih (para-yolu simetrisi).
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = cari, Tutar = 100m, Tarih = Gun(5) }));
    }

    [Fact]
    public async Task Dovizli_duzeltme_baz_paraya_cevrilir_ve_dengeli_kalir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "Dovizli");

        // Elle: 100 EUR × kur 40 = 4000 TL bakiye artışı.
        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 100m, Doviz = "EUR", Kur = 40m, Yon = BakiyeDuzeltmeYonu.Borclandir });

        Assert.Equal(4000m, await cash.GetCariBalanceAsync(cari));
        var satirlar = await DuzeltmeSatirlariAsync(host, tenant);
        Assert.Equal(
            satirlar.Where(x => x.Yon == LedgerDirection.Debit).Sum(x => x.Baz),
            satirlar.Where(x => x.Yon == LedgerDirection.Credit).Sum(x => x.Baz));
        Assert.All(satirlar, x => Assert.Equal(4000m, x.Baz));
    }

    [Fact]
    public async Task Duzeltme_tenant_izolasyonu_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid cari;
        using (var s1 = host.ScopeFor(t1)) cari = await CariAsync(s1, "T1");

        // Başka tenant'ın cari kimliğiyle düzeltme: cari bulunamaz.
        using (var s2 = host.ScopeFor(t2))
            await Assert.ThrowsAsync<ValidationException>(() => s2.ServiceProvider
                .GetRequiredService<BakiyeDuzeltmeService>()
                .AdjustAsync(new BakiyeDuzeltmeInput { CariId = cari, Tutar = 100m }));

        // Operatör düzeltme yapamaz (FinanceWrite yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<YetkiYokException>(() => op.ServiceProvider
            .GetRequiredService<BakiyeDuzeltmeService>()
            .AdjustAsync(new BakiyeDuzeltmeInput { CariId = cari, Tutar = 100m }));
    }

    [Fact]
    public async Task Duzeltme_cari_ekstresinde_gorunur()
    {
        // Kullanıcı düzeltmeyi bir daha göremezse "bakiyem neden değişti" sorusu cevapsız kalır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BakiyeDuzeltmeService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "Ekstre");

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = cari, Tutar = 75m, Yon = BakiyeDuzeltmeYonu.Borclandir, Aciklama = "Yuvarlama farkı" });

        var ekstre = await cash.GetStatementAsync(cari);
        Assert.Contains(ekstre.Satirlar, x => x.SourceType == BakiyeDuzeltmeService.Kaynak);
        Assert.Contains(ekstre.Satirlar, x => (x.Description ?? "").Contains("Yuvarlama farkı"));
    }
}
