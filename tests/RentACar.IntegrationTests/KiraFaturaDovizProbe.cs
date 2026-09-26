using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// ADVERSARIAL probe: kira dövizi → fatura → ledger TL. BAĞIMSIZ ORACLE (beklenen TL elle hesaplanır,
/// koddan türetilmez). Amaç ÇÜRÜTMEK: kur yönü, denge, tahsilat, eksik-kur, sabit-override, iade,
/// NormalizeKod, TL regresyon, idempotency, yuvarlama.
/// </summary>
[Collection("postgres")]
public sealed class KiraFaturaDovizProbe(PostgresFixture fx)
{
    private static BookingInput Rental(Guid account, Guid vehicle, string? currency, decimal daily = 100m, int day = 3) => new()
    {
        MusteriId = account,
        VehicleId = vehicle,
        BasTar = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero),
        BitTar = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero).AddDays(day),
        GunlukUcret = daily,
        Doviz = currency
    };

    private static async Task SeedTcmb(IDbContextFactory<AppDbContext> factory, string code, decimal sale, int unit = 1)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.KurKayitlari.Add(new KurKaydi
        {
            Kod = code,
            Ad = code,
            Birim = unit,
            Tarih = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), // fatura tarihinden (UtcNow) önce
            ForexSatis = sale,
            ForexAlis = sale,
            EfektifSatis = sale,
            EfektifAlis = sale
        });
        await db.SaveChangesAsync();
    }

    private static async Task<decimal[]> InvoiceEntries(IServiceProvider sp, Guid invId)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => (e.SourceType == "Fatura" || e.SourceType == "FaturaIade") && e.SourceId == invId).ToListAsync();
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        return [debit, credit];
    }

    // ---- 1. KUR YÖNÜ: EUR sayısı DEĞİL TL karşılığı BÜYÜK ----
    [Fact]
    public async Task P1_kur_yonu_EUR300_at40_yields_12000TL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "EURO")); // 3 gün × 100 = 300 EUR
        var invId = await invoices.CreateFromRentalAsync(rentalId);

        // ORACLE (elle): 300 EUR × 40 = 12000 TL. EUR-sayısı 300 DEĞİL.
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(account));
        Assert.NotEqual(300m, await cash.GetAccountBalanceAsync(account)); // 1:1 sızıntı olmamalı
    }

    // ---- 2. DENGE: garip kur (33.3333) + kuruşlu net/kdv ----
    [Fact]
    public async Task P2_denge_weird_kur_kurus_kacagi_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        // 1 gün × 100 = 100 EUR brüt; %20 → net 83.33, kdv 16.67 (16.67 = 100-83.33). Kur 33.3333.
        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 33.3333m, Aktif = true });
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "EUR", daily: 100m, day: 1));
        var invId = await invoices.CreateFromRentalAsync(rentalId); // denge guard patlarsa burada atar

        var e = await InvoiceEntries(scope.ServiceProvider, invId);
        // ORACLE: borç = 100 × 33.3333 = 3333.33; alacak = 83.33×33.3333 + 16.67×33.3333.
        Assert.Equal(3333.33m, e[0]);       // borç base
        Assert.Equal(e[0], e[1]);           // denge korunur (×kur sonrası)
        Assert.Equal(3333.33m, await cash.GetAccountBalanceAsync(account));
    }

    // ---- 3. TAHSİLAT: EUR faturaya TL tahsilat, tam + kısmi ----
    [Fact]
    public async Task P3_TL_tahsilat_EUR_faturayi_TL_bazinda_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "EUR"));
        await invoices.CreateFromRentalAsync(rentalId);
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(account));

        // Kısmi TL tahsilat 5000 → 7000 kalır (TL bazında).
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 5000m, Doviz = "TRY", Kur = 1m });
        Assert.Equal(7000m, await cash.GetAccountBalanceAsync(account));
        // Kalan 7000 → 0.
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 7000m, Doviz = "TRY", Kur = 1m });
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));
    }

    // ---- 4. EKSİK KUR: FX kira, HİÇ kur yok → temiz red — O5 sonrası red DAHA ERKEN (create anında) ----
    [Fact]
    public async Task P4_eksik_kur_temiz_red_sizinti_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        // İZOLE kod "CHF": paylaşımlı KurKayitlari tablosunda hiçbir test bunu seed etmez; sabit de yok.
        // O5 (KurSnapshot): kur çözülemeyen FX kira artık OLUŞTURMADA reddedilir (faturayı beklemez).
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var vehicle = await TestVehicle.NewAsync(scope.ServiceProvider); // gerçek araç: red kurdan gelmeli, varlık kontrolünden değil
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(Rental(account, vehicle, "CHF"))); // sabit YOK, TCMB YOK
        // Sessiz 1:1 (300) veya 0 borçlanma OLMAMALI — hiçbir şey yazılmadı.
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));
    }

    // ---- 5a. SABİT KUR fatura anında TCMB'yi EZER ----
    [Fact]
    public async Task P5a_sabit_kur_TCMByi_ezer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        // İZOLE kod "NOK" (paylaşımlı KurKayitlari çakışmasını önler).
        await SeedTcmb(factory, "NOK", 35m);                                   // TCMB 35
        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "NOK", Kur = 40m, Aktif = true }); // firma 40
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "NOK"));
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal(40m, inv!.Kur);                    // sabit kazanır
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(account)); // 300×40, 300×35=10500 DEĞİL
    }

    // ---- 5b. PASİF sabit kur → TCMB'ye düşer (yanlış uygulanmaz) ----
    [Fact]
    public async Task P5b_pasif_sabit_TCMBye_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        // İZOLE kod "SEK".
        await SeedTcmb(factory, "SEK", 35m);
        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "SEK", Kur = 40m, Aktif = false }); // PASİF
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "SEK"));
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal(35m, inv!.Kur);                    // TCMB kullanılır
        Assert.Equal(10500m, await cash.GetAccountBalanceAsync(account)); // 300×35
    }

    // ---- 6. İADE: EUR fatura + iade → cari TL bazında 0 ----
    [Fact]
    public async Task P6_iade_EUR_cari_sifirlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "EUR"));
        var invId = await invoices.CreateFromRentalAsync(rentalId);
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(account));

        var refundId = await invoices.CreateRefundAsync(invId);
        var refund = await invoices.GetAsync(refundId);
        Assert.Equal("EUR", refund!.Currency);
        Assert.Equal(40m, refund.Kur);
        // ORACLE: +12000 − 12000 = 0.
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));
        var e = await InvoiceEntries(scope.ServiceProvider, refundId);
        Assert.Equal(e[0], e[1]); // iade dengeli
    }

    // ---- 7. NormalizeKod: sabit "EURO" tanımlı, fatura "EUR" arar → eşleşir; unknown red ----
    [Fact]
    public async Task P7a_sabit_EURO_ile_tanimli_fatura_EUR_eslesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EURO", Kur = 40m, Aktif = true }); // "EURO" → normalize EUR
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "euro")); // küçük harf
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal("EUR", inv!.Currency);
        Assert.Equal(40m, inv.Kur);                     // sabit ATLANMADI
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(account));
    }

    [Fact]
    public void P7b_NormalizeKod_variantlar()
    {
        Assert.Equal("EUR", ExchangeRateService.NormalizeCode("EURO"));
        Assert.Equal("EUR", ExchangeRateService.NormalizeCode("euro"));
        Assert.Equal("EUR", ExchangeRateService.NormalizeCode("€"));
        Assert.Equal("TRY", ExchangeRateService.NormalizeCode(" TL "));
        Assert.Equal("TRY", ExchangeRateService.NormalizeCode(null));
        Assert.Equal("TRY", ExchangeRateService.NormalizeCode(""));
        Assert.Equal("USD", ExchangeRateService.NormalizeCode("Usd"));
        Assert.Equal("USD", ExchangeRateService.NormalizeCode("dolar"));
        Assert.Equal("XAU", ExchangeRateService.NormalizeCode("xau")); // bilinmeyen → olduğu gibi (upper)
    }

    [Fact]
    public async Task P7c_bilinmeyen_doviz_XAU_kur_yok_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        // O5: kur çözülemeyen bilinmeyen döviz artık kira OLUŞTURMADA reddedilir.
        var vehicle = await TestVehicle.NewAsync(scope.ServiceProvider); // gerçek araç: red kurdan gelmeli, varlık kontrolünden değil
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(Rental(account, vehicle, "XAU")));
    }

    // ---- 8. TL REGRESYON: Doviz null / "TL" → TRY, Kur 1, 1:1 ----
    [Theory]
    [InlineData(null)]
    [InlineData("TL")]
    [InlineData("TRY")]
    public async Task P8_TL_regresyon(string? currency)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), currency));
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal("TRY", inv!.Currency);
        Assert.Equal(1m, inv.Kur);
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(account)); // 1:1
    }

    // ---- 9. İDEMPOTENCY: aynı EUR kirayı 2× faturala → tek borç ----
    [Fact]
    public async Task P9_idempotency_cift_fatura_tek_borc()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "EUR"));
        await invoices.CreateFromRentalAsync(rentalId);
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(account)); // 24000 DEĞİL
    }

    // ---- 10. Büyük tutar × 6-dp kur: denge korunur, cari TL, guard patlamaz ----
    [Fact]
    public async Task P10_buyuk_tutar_6dp_kur_denge_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var fixedValue = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        // 1 gün × 1,000,000 = 1,000,000 EUR brüt; kur 6-dp garip 41.123456.
        await fixedValue.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 41.123456m, Aktif = true });
        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(account, await TestVehicle.NewAsync(scope.ServiceProvider), "EUR", daily: 1_000_000m, day: 1));
        var invId = await invoices.CreateFromRentalAsync(rentalId); // denge guard patlarsa atar

        var e = await InvoiceEntries(scope.ServiceProvider, invId);
        Assert.Equal(e[0], e[1]);                       // denge korunur
        // ORACLE: 1,000,000 × 41.123456 = 41,123,456.
        Assert.Equal(41_123_456m, e[0]);
        Assert.Equal(41_123_456m, await cash.GetAccountBalanceAsync(account));
    }
}
