using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-A dönem-sonu kapanış fişi (close-lite). BAĞIMSIZ ORACLE: gelir 1000 − gider 300 = 700 (elle kurulmuş,
/// koddan DEĞİL). Kapanış fişi Gelir/Gider'i sıfırlar, net'i DonemSonucu'na taşır; P&amp;L raporları (GelirGider,
/// Karlılık) kapanıştan ETKİLENMEZ (fiş iç virman → hariç tutulur); çift-taraflı defter dengesi korunur; idempotent.
/// </summary>
[Collection("postgres")]
public sealed class DonemKapanisFisiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset BusinessDate = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero); // işlem tarihi
    private static readonly DateTimeOffset Closing = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero); // kapanış tarihi

    /// <summary>Gelir 1000 (KDV 0 → Gelir defter 1000) + Gider 300 (KDV 0 → Gider 300) tohumu.</summary>
    private static async Task SeedAsync(IServiceProvider sp, decimal revenueNet, decimal expenseNet)
    {
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Kapanış Test" });
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = account, NetTutar = revenueNet, KdvOrani = 0m, Tarih = BusinessDate });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = expenseNet, KdvOrani = 0m, Doviz = "TRY", Kur = 1m,
            OdemeYontemi = PaymentMethod.Nakit, Tarih = BusinessDate
        });
    }

    [Fact]
    public async Task Kapanis_gelir_gider_sifirlar_net_donem_sonucuna_tasir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 1000m, 300m);
        var report = sp.GetRequiredService<ReportService>();

        // Kapanış ÖNCESİ P&L (elle: 1000 − 300 = 700).
        var ggOnce = await report.GetRevenueExpenseAsync();
        Assert.Equal(1000m, ggOnce.GelirToplam);
        Assert.Equal(300m, ggOnce.GiderToplam);
        Assert.Equal(700m, ggOnce.NetKar);

        await sp.GetRequiredService<PeriodClosingVoucherService>().CloseAsync(Closing);

        // Mizan: Gelir/Gider sıfırlanmış, DonemSonucu = −700 (Credit bakiye = kâr → özkaynak).
        var trialBalance = await report.GetTrialBalanceAsync();
        Assert.Equal(0m, Balance(trialBalance, LedgerAccountType.Gelir));
        Assert.Equal(0m, Balance(trialBalance, LedgerAccountType.Gider));
        Assert.Equal(-700m, Balance(trialBalance, LedgerAccountType.DonemSonucu));
        Assert.Equal(0m, trialBalance.Sum(m => m.Bakiye)); // çift-taraflı defter dengesi

        // P&L raporları kapanıştan ETKİLENMEZ (fiş SourceType='DonemKapanis' → hariç).
        var plAfter = await report.GetRevenueExpenseAsync();
        Assert.Equal(1000m, plAfter.GelirToplam);
        Assert.Equal(300m, plAfter.GiderToplam);
        Assert.Equal(700m, plAfter.NetKar);
        Assert.Equal(700m, (await report.GetProfitabilityAsync()).ToplamNetKar);

        // Dönem kilitli.
        var lockEntry = await sp.GetRequiredService<PeriodLockService>().GetClosingDateAsync();
        Assert.Equal(Closing.Date, lockEntry!.Value.Date);
    }

    [Fact]
    public async Task Kapanis_idempotent_yeniden_kapatma_cift_saymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 1000m, 300m);
        var closing = sp.GetRequiredService<PeriodClosingVoucherService>();
        var period = sp.GetRequiredService<PeriodLockService>();
        var report = sp.GetRequiredService<ReportService>();

        await closing.CloseAsync(Closing);
        await period.UnlockAsync();
        await closing.CloseAsync(Closing); // AYNI tarih tekrar → fiş NO-OP (deterministik SourceId + unique index)

        var trialBalance = await report.GetTrialBalanceAsync();
        Assert.Equal(-700m, Balance(trialBalance, LedgerAccountType.DonemSonucu)); // −1400 DEĞİL (çift saymadı)
        Assert.Equal(0m, Balance(trialBalance, LedgerAccountType.Gelir));
        Assert.Equal(0m, trialBalance.Sum(m => m.Bakiye));
    }

    [Fact]
    public async Task Zarar_donemi_donem_sonucu_borc_bakiye()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 300m, 1000m); // gelir 300 < gider 1000 → zarar 700

        await sp.GetRequiredService<PeriodClosingVoucherService>().CloseAsync(Closing);

        var trialBalance = await sp.GetRequiredService<ReportService>().GetTrialBalanceAsync();
        Assert.Equal(700m, Balance(trialBalance, LedgerAccountType.DonemSonucu)); // zarar → Borç bakiye +700
        Assert.Equal(0m, trialBalance.Sum(m => m.Bakiye));
    }

    [Fact]
    public async Task Cok_donem_kapanis_delta_yakalar_cift_saymaz()
    {
        // ADVERSARIAL regresyon: art arda iki dönem kapatılınca ikinci kapanış YALNIZ yeni delta'yı yakalamalı
        // (ilk kapanış o güne dek olanı zaten sıfırladı). Aksi halde ilk dönem gelirini çift sayardı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Çok Dönem" });
        var inv = sp.GetRequiredService<InvoiceService>();
        var exp = sp.GetRequiredService<ExpenseService>();
        var closing = sp.GetRequiredService<PeriodClosingVoucherService>();
        var report = sp.GetRequiredService<ReportService>();

        var mayDate = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero);
        var mayClosing = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero);
        var juneDate = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var juneClosing = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

        // Mayıs: gelir 1000 → kapat (kâr 1000).
        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = account, NetTutar = 1000m, KdvOrani = 0m, Tarih = mayDate });
        await closing.CloseAsync(mayClosing);
        Assert.Equal(-1000m, Balance(await report.GetTrialBalanceAsync(), LedgerAccountType.DonemSonucu));

        // Haziran: gelir 500 − gider 200 = 300 → kapat. DonemSonucu = −(1000 + 300) = −1300.
        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = account, NetTutar = 500m, KdvOrani = 0m, Tarih = juneDate });
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 200m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = PaymentMethod.Nakit, Tarih = juneDate });
        await closing.CloseAsync(juneClosing);

        var trialBalance = await report.GetTrialBalanceAsync();
        Assert.Equal(-1300m, Balance(trialBalance, LedgerAccountType.DonemSonucu)); // −2300 (çift-sayım) DEĞİL
        Assert.Equal(0m, Balance(trialBalance, LedgerAccountType.Gelir));
        Assert.Equal(0m, Balance(trialBalance, LedgerAccountType.Gider));
        Assert.Equal(0m, trialBalance.Sum(m => m.Bakiye));
        // P&L toplam kapanışlardan etkilenmez: gelir 1500, gider 200, net 1300.
        Assert.Equal(1300m, (await report.GetRevenueExpenseAsync()).NetKar);
    }

    [Fact]
    public async Task Zaten_kapali_donem_yeniden_kapatilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 1000m, 300m);
        var closing = sp.GetRequiredService<PeriodClosingVoucherService>();

        await closing.CloseAsync(Closing);
        // Kilit dururken aynı/önceki tarihi tekrar kapatmak reddedilir (yanlış çift-kapanış önlenir).
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() => closing.CloseAsync(Closing));
    }

    private static decimal Balance(IReadOnlyList<MizanSatirDto> trialBalance, LedgerAccountType t)
        => trialBalance.FirstOrDefault(m => m.Tip == t)?.Bakiye ?? 0m;
}
