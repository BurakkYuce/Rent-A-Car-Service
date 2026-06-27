using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Fiyat motoru v1 — bağımsız oracle. GunlukUcret==0 ise araç grubu + gün kademesi + tarihten
/// tarife lookup; manuel ücret (>0) kazanır; eşleşme yoksa 0. Beklenen değerler elle hesaplı.
/// Üç booking yolu (rezervasyon/teklif/kira) auto-fiyatı sözleşmeye yazar.
/// </summary>
[Collection("postgres")]
public sealed class PricingTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Bit(int gun) => Bas.AddDays(gun);

    /// <summary>Araç (grup) + cari + iki kademe tarife (B: 1–3→100, 4+→80) tohumlar.</summary>
    private static async Task<(Guid musteri, Guid arac)> SeedAsync(IServiceScope s, string grup = "B")
    {
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var customers = s.ServiceProvider.GetRequiredService<CustomerService>();
        var rates = s.ServiceProvider.GetRequiredService<RateCardService>();
        var arac = await vehicles.CreateAsync(new VehicleInput { Plaka = "34PRC01", Grup = grup });
        var musteri = await customers.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Fiyat", Soyad = "Test" });
        await rates.CreateAsync(new RateCardInput { Kod = "B1", Ad = "B 1-3", Grup = "B", MinGun = 1, MaxGun = 3, GunlukUcret = 100m });
        await rates.CreateAsync(new RateCardInput { Kod = "B2", Ad = "B 4+", Grup = "B", MinGun = 4, MaxGun = 9999, GunlukUcret = 80m });
        return (musteri, arac);
    }

    private static BookingInput Booking(Guid m, Guid v, int gun, decimal gunluk = 0m) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bit(gun), GunlukUcret = gunluk
    };

    [Fact]
    public async Task Reservation_auto_prices_from_ratecard_by_day_tier()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        // 2 gün, ücret verilmedi → B 1-3 kademesi (100). 2×100=200.
        var id = await rez.CreateAsync(Booking(m, v, 2));
        var r = await rez.GetAsync(id);
        Assert.Equal(2, r!.Gun);
        Assert.Equal(100m, r.GunlukUcret);
        Assert.Equal(200m, r.Tutar);
    }

    [Fact]
    public async Task Reservation_picks_correct_tier_for_longer_period()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        // 5 gün → B 4+ kademesi (80). 5×80=400.
        var id = await rez.CreateAsync(Booking(m, v, 5));
        var r = await rez.GetAsync(id);
        Assert.Equal(5, r!.Gun);
        Assert.Equal(80m, r.GunlukUcret);
        Assert.Equal(400m, r.Tutar);
    }

    [Fact]
    public async Task Manual_rate_overrides_ratecard()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        // Manuel 150 verildi → tarife (100) yok sayılır. 2×150=300.
        var id = await rez.CreateAsync(Booking(m, v, 2, gunluk: 150m));
        var r = await rez.GetAsync(id);
        Assert.Equal(150m, r!.GunlukUcret);
        Assert.Equal(300m, r.Tutar);
    }

    [Fact]
    public async Task No_matching_ratecard_leaves_zero()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        // Grup "Z" → tarife yok.
        var (m, v) = await SeedAsync(scope, grup: "Z");

        var id = await rez.CreateAsync(Booking(m, v, 2));
        var r = await rez.GetAsync(id);
        Assert.Equal(0m, r!.GunlukUcret);
        Assert.Equal(0m, r.Tutar);
    }

    [Fact]
    public async Task Quotation_auto_prices_and_persists_effective_rate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var teklif = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (m, v) = await SeedAsync(scope);

        var input = new QuotationInput { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bit(2), GunlukUcret = 0m };
        var id = await teklif.CreateAsync(input);
        var q = await teklif.GetAsync(id);
        // Efektif ücret tekliften okunabilmeli (writeback doğru).
        Assert.Equal(100m, q!.GunlukUcret);
        Assert.Equal(200m, q.Tutar);
    }

    [Fact]
    public async Task Direct_rental_auto_prices()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        var id = await kira.CreateDirectAsync(Booking(m, v, 2));
        var r = await kira.GetAsync(id);
        Assert.Equal(100m, r!.GunlukUcret);
        Assert.Equal(200m, r.Tutar);
        Assert.Equal(200m, r.GenelToplam);
        Assert.Equal(200m, r.Bakiye);
    }

    [Fact]
    public async Task PricingService_resolves_rate_by_group_or_zero()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var pricing = scope.ServiceProvider.GetRequiredService<PricingService>();
        var (_, v) = await SeedAsync(scope);

        Assert.Equal(100m, await pricing.ResolveDailyRateAsync(v, 2, Bas));   // B, 2 gün
        Assert.Equal(80m, await pricing.ResolveDailyRateAsync(v, 7, Bas));    // B, 7 gün → 4+ kademe
        Assert.Equal(0m, await pricing.ResolveDailyRateAsync(Guid.NewGuid(), 2, Bas)); // araç yok → 0
    }
}
