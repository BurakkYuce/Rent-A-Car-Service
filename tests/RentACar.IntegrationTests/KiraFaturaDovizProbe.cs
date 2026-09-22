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
    private static BookingInput Rental(Guid cari, Guid vehicle, string? doviz, decimal gunluk = 100m, int gun = 3) => new()
    {
        MusteriId = cari,
        VehicleId = vehicle,
        BasTar = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero),
        BitTar = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero).AddDays(gun),
        GunlukUcret = gunluk,
        Doviz = doviz
    };

    private static async Task SeedTcmb(IDbContextFactory<AppDbContext> factory, string kod, decimal satis, int birim = 1)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.KurKayitlari.Add(new KurKaydi
        {
            Kod = kod,
            Ad = kod,
            Birim = birim,
            Tarih = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), // fatura tarihinden (UtcNow) önce
            ForexSatis = satis,
            ForexAlis = satis,
            EfektifSatis = satis,
            EfektifAlis = satis
        });
        await db.SaveChangesAsync();
    }

    private static async Task<decimal[]> FaturaEntries(IServiceProvider sp, Guid invId)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => (e.SourceType == "Fatura" || e.SourceType == "FaturaIade") && e.SourceId == invId).ToListAsync();
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        return [borc, alacak];
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "EURO")); // 3 gün × 100 = 300 EUR
        var invId = await invoices.CreateFromRentalAsync(rentalId);

        // ORACLE (elle): 300 EUR × 40 = 12000 TL. EUR-sayısı 300 DEĞİL.
        Assert.Equal(12000m, await cash.GetCariBalanceAsync(cari));
        Assert.NotEqual(300m, await cash.GetCariBalanceAsync(cari)); // 1:1 sızıntı olmamalı
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        // 1 gün × 100 = 100 EUR brüt; %20 → net 83.33, kdv 16.67 (16.67 = 100-83.33). Kur 33.3333.
        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 33.3333m, Aktif = true });
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "EUR", gunluk: 100m, gun: 1));
        var invId = await invoices.CreateFromRentalAsync(rentalId); // denge guard patlarsa burada atar

        var e = await FaturaEntries(scope.ServiceProvider, invId);
        // ORACLE: borç = 100 × 33.3333 = 3333.33; alacak = 83.33×33.3333 + 16.67×33.3333.
        Assert.Equal(3333.33m, e[0]);       // borç base
        Assert.Equal(e[0], e[1]);           // denge korunur (×kur sonrası)
        Assert.Equal(3333.33m, await cash.GetCariBalanceAsync(cari));
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var cari = await TestCari.YeniAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "EUR"));
        await invoices.CreateFromRentalAsync(rentalId);
        Assert.Equal(12000m, await cash.GetCariBalanceAsync(cari));

        // Kısmi TL tahsilat 5000 → 7000 kalır (TL bazında).
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 5000m, Doviz = "TRY", Kur = 1m });
        Assert.Equal(7000m, await cash.GetCariBalanceAsync(cari));
        // Kalan 7000 → 0.
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 7000m, Doviz = "TRY", Kur = 1m });
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
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
        var cari = Guid.NewGuid();
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "CHF"))); // sabit YOK, TCMB YOK
        // Sessiz 1:1 (300) veya 0 borçlanma OLMAMALI — hiçbir şey yazılmadı.
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        // İZOLE kod "NOK" (paylaşımlı KurKayitlari çakışmasını önler).
        await SeedTcmb(factory, "NOK", 35m);                                   // TCMB 35
        await sabit.UpsertAsync(new SabitKurInput { Kod = "NOK", Kur = 40m, Aktif = true }); // firma 40
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "NOK"));
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal(40m, inv!.Kur);                    // sabit kazanır
        Assert.Equal(12000m, await cash.GetCariBalanceAsync(cari)); // 300×40, 300×35=10500 DEĞİL
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        // İZOLE kod "SEK".
        await SeedTcmb(factory, "SEK", 35m);
        await sabit.UpsertAsync(new SabitKurInput { Kod = "SEK", Kur = 40m, Aktif = false }); // PASİF
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "SEK"));
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal(35m, inv!.Kur);                    // TCMB kullanılır
        Assert.Equal(10500m, await cash.GetCariBalanceAsync(cari)); // 300×35
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "EUR"));
        var invId = await invoices.CreateFromRentalAsync(rentalId);
        Assert.Equal(12000m, await cash.GetCariBalanceAsync(cari));

        var iadeId = await invoices.CreateIadeAsync(invId);
        var iade = await invoices.GetAsync(iadeId);
        Assert.Equal("EUR", iade!.Currency);
        Assert.Equal(40m, iade.Kur);
        // ORACLE: +12000 − 12000 = 0.
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
        var e = await FaturaEntries(scope.ServiceProvider, iadeId);
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        await sabit.UpsertAsync(new SabitKurInput { Kod = "EURO", Kur = 40m, Aktif = true }); // "EURO" → normalize EUR
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "euro")); // küçük harf
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal("EUR", inv!.Currency);
        Assert.Equal(40m, inv.Kur);                     // sabit ATLANMADI
        Assert.Equal(12000m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public void P7b_NormalizeKod_variantlar()
    {
        Assert.Equal("EUR", KurService.NormalizeKod("EURO"));
        Assert.Equal("EUR", KurService.NormalizeKod("euro"));
        Assert.Equal("EUR", KurService.NormalizeKod("€"));
        Assert.Equal("TRY", KurService.NormalizeKod(" TL "));
        Assert.Equal("TRY", KurService.NormalizeKod(null));
        Assert.Equal("TRY", KurService.NormalizeKod(""));
        Assert.Equal("USD", KurService.NormalizeKod("Usd"));
        Assert.Equal("USD", KurService.NormalizeKod("dolar"));
        Assert.Equal("XAU", KurService.NormalizeKod("xau")); // bilinmeyen → olduğu gibi (upper)
    }

    [Fact]
    public async Task P7c_bilinmeyen_doviz_XAU_kur_yok_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var cari = Guid.NewGuid();
        // O5: kur çözülemeyen bilinmeyen döviz artık kira OLUŞTURMADA reddedilir.
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "XAU")));
    }

    // ---- 8. TL REGRESYON: Doviz null / "TL" → TRY, Kur 1, 1:1 ----
    [Theory]
    [InlineData(null)]
    [InlineData("TL")]
    [InlineData("TRY")]
    public async Task P8_TL_regresyon(string? doviz)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), doviz));
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal("TRY", inv!.Currency);
        Assert.Equal(1m, inv.Kur);
        Assert.Equal(300m, await cash.GetCariBalanceAsync(cari)); // 1:1
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "EUR"));
        await invoices.CreateFromRentalAsync(rentalId);
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(rentalId));
        Assert.Equal(12000m, await cash.GetCariBalanceAsync(cari)); // 24000 DEĞİL
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
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();

        // 1 gün × 1,000,000 = 1,000,000 EUR brüt; kur 6-dp garip 41.123456.
        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 41.123456m, Aktif = true });
        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid(), "EUR", gunluk: 1_000_000m, gun: 1));
        var invId = await invoices.CreateFromRentalAsync(rentalId); // denge guard patlarsa atar

        var e = await FaturaEntries(scope.ServiceProvider, invId);
        Assert.Equal(e[0], e[1]);                       // denge korunur
        // ORACLE: 1,000,000 × 41.123456 = 41,123,456.
        Assert.Equal(41_123_456m, e[0]);
        Assert.Equal(41_123_456m, await cash.GetCariBalanceAsync(cari));
    }
}
