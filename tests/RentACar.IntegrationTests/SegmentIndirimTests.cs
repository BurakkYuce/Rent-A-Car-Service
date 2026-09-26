using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A2 — Müşteri segment indirimi: RentalRule.MusteriSegment (null = herkes) Customer.Sinif ile
/// Trim+case-insensitive eşleşir; segment cariden ORTAK facade'da (PricingService) çözülür.
/// BAĞIMSIZ ORACLE (elle): düz 1000/gün, 3 gün → ara 3000. {(VIP,%15),(genel,%10)}: VIP → 2550,
/// Sinif yok → 2700. SIRALAMA KRİTİK: {(Problemli,%0),(genel,%10)} → Problemli 3000 (%0 kural,
/// cömert genel kuralı YENER — segment-birebir eşleşme fayda kıyasından önce). Normalizasyon:
/// " vip " kaydı (Trim) + "VIP" kural eşleşir. Reprice semantiği: rezervasyon güncellemesi carinin
/// GÜNCEL sınıfını kullanır (VIP→sınıfsız değişince 2550→2700) — testle dokümante.
/// </summary>
[Collection("postgres")]
public sealed class SegmentIndirimTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task SeedAsync(IServiceProvider sp, params (string? Segment, decimal Iskonto)[] kurallar)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5.00m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
        });
        var rr = sp.GetRequiredService<RentalRuleService>();
        var i = 0;
        foreach (var (segment, iskonto) in kurallar)
            await rr.CreateAsync(new RentalRuleInput
            { Kod = $"R{++i}", Ad = $"Kural {i}", MusteriSegment = segment, Iskonto = iskonto });
    }

    private static Task<QuoteResult> TeklifAsync(IServiceProvider sp, string? segment) =>
        sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas.AddDays(3), MusteriSegment = segment });

    [Fact]
    public async Task Vip_ozel_kural_genel_kurali_ezer_sinifsiz_genel_alir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, ("VIP", 15m), (null, 10m));

        Assert.Equal(2550.00m, (await TeklifAsync(sp, "VIP")).GenelToplam);   // 3000 × 0.85 (elle)
        Assert.Equal(2700.00m, (await TeklifAsync(sp, null)).GenelToplam);    // 3000 × 0.90 (genel)
        // Kapsamı tutmayan segment: VIP kuralı elenmeli, genel uygulanmalı.
        Assert.Equal(2700.00m, (await TeklifAsync(sp, "Orta")).GenelToplam);
    }

    [Fact]
    public async Task Problemli_sifir_iskonto_comert_genel_kurali_yener() // sıralama kritik
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, ("Problemli", 0m), (null, 10m));

        // Segment-birebir eşleşme fayda kıyasından ÖNCE: %0'lık Problemli kuralı %10 geneli yener.
        var q = await TeklifAsync(sp, "Problemli");
        Assert.Equal(0m, q.IskontoOran);
        Assert.Equal(3000.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Facade_cariden_cozer_ve_normalizasyon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, ("VIP", 15m), (null, 10m));
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 SG 01", Grup = "EKO" });
        // Kayıtta Trim normalize (" vip " → "vip"); eşleşme case-insensitive → "VIP" kuralı tutar.
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Seg", Soyad = "M", Sinif = " vip " });

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), FiyatTuru = "Otomatik" });
        Assert.Equal(2550.00m, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.Tutar);
    }

    [Fact]
    public async Task Reprice_carinin_guncel_sinifini_kullanir() // semantik dokümantasyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, ("VIP", 15m), (null, 10m));
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 SG 02", Grup = "EKO" });
        var musteriler = sp.GetRequiredService<CustomerService>();
        var m = await musteriler.CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Seg", Soyad = "R", Sinif = "VIP" });

        BookingInput Girdi() => new()
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), FiyatTuru = "Otomatik" };
        var rez = sp.GetRequiredService<ReservationService>();
        var id = await rez.CreateAsync(Girdi());
        Assert.Equal(2550.00m, (await rez.GetAsync(id))!.Tutar);              // VIP fiyatı

        // Cari sınıfı düşürülür → rezervasyon güncellemesi GÜNCEL sınıfla yeniden fiyatlar.
        await musteriler.UpdateAsync(m, new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Seg", Soyad = "R", Sinif = null });
        await rez.UpdateAsync(id, Girdi());
        Assert.Equal(2700.00m, (await rez.GetAsync(id))!.Tutar);              // genel fiyat
    }
}
