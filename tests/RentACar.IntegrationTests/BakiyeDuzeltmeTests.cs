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
    private static DateTimeOffset Day(int difference)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(difference), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> CustomerAsync(IServiceScope s, string name)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });

    /// <summary>Cariye 1000 TL borç yükler (ödeme: Borç Cari / Alacak Kasa).</summary>
    private static async Task Debit(IServiceScope s, Guid account, decimal amount = 1000m)
    {
        var cash = await s.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = $"K{Guid.NewGuid():N}"[..8], Ad = "Kasa", Tur = "Kasa" });
        await s.ServiceProvider.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = account, Tutar = amount, Hesap = LedgerAccountType.Kasa, HesapId = cash });
    }

    private static async Task<List<(LedgerAccountType Tur, LedgerDirection Yon, decimal Baz)>>
        AdjustmentLinesAsync(TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == BalanceAdjustmentService.Source)
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
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await CustomerAsync(scope, "Alacak");
        await Debit(scope, account);   // 1000 borç

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 150m, Yon = BalanceAdjustmentDirection.Alacaklandir });

        // Elle: 1000 − 150 = 850.
        Assert.Equal(850m, await cash.GetAccountBalanceAsync(account));

        var rows = await AdjustmentLinesAsync(host, tenant);
        Assert.Equal(2, rows.Count);
        // Cari ALACAK (bakiye düşer) / karşı bacak Muhasebe Düzeltmesi BORÇ.
        Assert.Contains(rows, x => x.Tur == LedgerAccountType.Cari
            && x.Yon == LedgerDirection.Credit && x.Baz == 150m);
        Assert.Contains(rows, x => x.Tur == LedgerAccountType.MuhasebeDuzeltmesi
            && x.Yon == LedgerDirection.Debit && x.Baz == 150m);
        // Denge.
        Assert.Equal(
            rows.Where(x => x.Yon == LedgerDirection.Debit).Sum(x => x.Baz),
            rows.Where(x => x.Yon == LedgerDirection.Credit).Sum(x => x.Baz));
    }

    [Fact]
    public async Task Borclandirma_bakiyeyi_YUKSELTIR()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await CustomerAsync(scope, "Borc");
        await Debit(scope, account);   // 1000

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 250m, Yon = BalanceAdjustmentDirection.Borclandir });

        // Elle: 1000 + 250 = 1250.
        Assert.Equal(1250m, await cash.GetAccountBalanceAsync(account));
        var rows = await AdjustmentLinesAsync(host, tenant);
        Assert.Contains(rows, x => x.Tur == LedgerAccountType.Cari && x.Yon == LedgerDirection.Debit);
        Assert.Contains(rows, x => x.Tur == LedgerAccountType.MuhasebeDuzeltmesi && x.Yon == LedgerDirection.Credit);
    }

    [Fact]
    public async Task Ters_yonlu_ikinci_duzeltme_ilkini_kapatir()
    {
        // Defter değişmezdir: yanlış düzeltme SİLİNMEZ, ters kayıtla kapatılır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await CustomerAsync(scope, "TersKayit");
        await Debit(scope, account);

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 300m, Yon = BalanceAdjustmentDirection.Alacaklandir });
        Assert.Equal(700m, await cash.GetAccountBalanceAsync(account));

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 300m, Yon = BalanceAdjustmentDirection.Borclandir });
        Assert.Equal(1000m, await cash.GetAccountBalanceAsync(account));
    }

    // ---------------------------------------------------------------- P&L kirlenmiyor (kararın özü)

    [Fact]
    public async Task Duzeltme_GELIR_GIDER_ve_KARLILIK_toplamlarina_KARISMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "PnL");
        await Debit(scope, account);

        var ggOnce = await reports.GetRevenueExpenseAsync();
        var scorecardBefore = await reports.GetFleetAnalysisAsync();

        // UÇUK bir düzeltme: Gelir/Gider'e yazılsaydı raporlar şişerdi.
        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 999_999m, Yon = BalanceAdjustmentDirection.Alacaklandir });

        var plAfter = await reports.GetRevenueExpenseAsync();
        Assert.Equal(ggOnce.GelirToplam, plAfter.GelirToplam);
        Assert.Equal(ggOnce.GiderToplam, plAfter.GiderToplam);
        Assert.Equal(ggOnce.NetKar, plAfter.NetKar);

        var scorecardAfter = await reports.GetFleetAnalysisAsync();
        Assert.Equal(scorecardBefore.Satirlar.Sum(x => x.Gelir), scorecardAfter.Satirlar.Sum(x => x.Gelir));
        Assert.Equal(scorecardBefore.Satirlar.Sum(x => x.Gider), scorecardAfter.Satirlar.Sum(x => x.Gider));

        // …ama cari bakiyesi GERÇEKTEN değişti (düzeltme işini yaptı).
        Assert.Equal(1000m - 999_999m,
            await scope.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(account));
    }

    [Fact]
    public async Task Duzeltme_KASA_BANKA_bakiyesine_DOKUNMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Kasa");
        await Debit(scope, account);   // kasa −1000

        var once = await reports.GetCashBankSummaryAsync();
        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 400m, Yon = BalanceAdjustmentDirection.Alacaklandir });
        var after = await reports.GetCashBankSummaryAsync();

        Assert.Equal(once.KasaBakiye, after.KasaBakiye);
        Assert.Equal(once.BankaBakiye, after.BankaBakiye);
        Assert.Equal(-1000m, after.KasaBakiye);   // elle: yalnız ödeme etkisi
    }

    // ---------------------------------------------------------------- Adversarial kilitler

    [Fact]
    public async Task Cift_submit_yutulur_bakiye_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await CustomerAsync(scope, "CiftSubmit");
        await Debit(scope, account);

        var key = Guid.NewGuid();
        var input = () => new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 200m, Yon = BalanceAdjustmentDirection.Alacaklandir, IslemAnahtari = key };
        await svc.AdjustAsync(input());
        await svc.AdjustAsync(input());   // aynı token → yutulur

        // Elle: 1000 − 200 = 800 (600 DEĞİL).
        Assert.Equal(800m, await cash.GetAccountBalanceAsync(account));
    }

    [Fact]
    public async Task Gecersiz_girdiler_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var account = await CustomerAsync(scope, "Gecersiz");

        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = account, Tutar = 0m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = account, Tutar = -50m }));
        // Uydurma cari.
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = Guid.NewGuid(), Tutar = 100m }));
        // Gelecek tarih (para-yolu simetrisi).
        await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(
            new BakiyeDuzeltmeInput { CariId = account, Tutar = 100m, Tarih = Day(5) }));
    }

    [Fact]
    public async Task Dovizli_duzeltme_baz_paraya_cevrilir_ve_dengeli_kalir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await CustomerAsync(scope, "Dovizli");

        // Elle: 100 EUR × kur 40 = 4000 TL bakiye artışı.
        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 100m, Doviz = "EUR", Kur = 40m, Yon = BalanceAdjustmentDirection.Borclandir });

        Assert.Equal(4000m, await cash.GetAccountBalanceAsync(account));
        var rows = await AdjustmentLinesAsync(host, tenant);
        Assert.Equal(
            rows.Where(x => x.Yon == LedgerDirection.Debit).Sum(x => x.Baz),
            rows.Where(x => x.Yon == LedgerDirection.Credit).Sum(x => x.Baz));
        Assert.All(rows, x => Assert.Equal(4000m, x.Baz));
    }

    [Fact]
    public async Task Duzeltme_tenant_izolasyonu_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid account;
        using (var s1 = host.ScopeFor(t1)) account = await CustomerAsync(s1, "T1");

        // Başka tenant'ın cari kimliğiyle düzeltme: cari bulunamaz.
        using (var s2 = host.ScopeFor(t2))
            await Assert.ThrowsAsync<ValidationException>(() => s2.ServiceProvider
                .GetRequiredService<BalanceAdjustmentService>()
                .AdjustAsync(new BakiyeDuzeltmeInput { CariId = account, Tutar = 100m }));

        // Operatör düzeltme yapamaz (FinanceWrite yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(() => op.ServiceProvider
            .GetRequiredService<BalanceAdjustmentService>()
            .AdjustAsync(new BakiyeDuzeltmeInput { CariId = account, Tutar = 100m }));
    }

    [Fact]
    public async Task Duzeltme_cari_ekstresinde_gorunur()
    {
        // Kullanıcı düzeltmeyi bir daha göremezse "bakiyem neden değişti" sorusu cevapsız kalır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BalanceAdjustmentService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await CustomerAsync(scope, "Ekstre");

        await svc.AdjustAsync(new BakiyeDuzeltmeInput
        { CariId = account, Tutar = 75m, Yon = BalanceAdjustmentDirection.Borclandir, Aciklama = "Yuvarlama farkı" });

        var statement = await cash.GetStatementAsync(account);
        Assert.Contains(statement.Satirlar, x => x.SourceType == BalanceAdjustmentService.Source);
        Assert.Contains(statement.Satirlar, x => (x.Description ?? "").Contains("Yuvarlama farkı"));
    }
}
