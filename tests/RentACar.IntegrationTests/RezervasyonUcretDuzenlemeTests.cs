using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #361 adversarial P7 — ELLE açılmış net/toplam modlu rezervasyonlarda da düzenleme formundaki "Günlük ücret"
/// alanı (kayıtlı GÜNLÜK BRÜT) değiştirilince girilen değer günlük brüt sayılır; site talebi yoluyla AYNI davranış.
/// ELLE ORACLE (3 gün, tenant KDV %20):
/// - "Toplam" 4.500 net toplam → brüt 5.400 (günlük 1.800); ücret 1.700'e çekilir → 3 × 1.700 = 5.100.
/// - "KDV Dahil Toplam" 5.400 brüt toplam → günlük 1.800; ücret 1.700 → 5.100.
/// - "Günlük" 1.000 net günlük → günlük brüt 1.200, tutar 3.600; ücret 1.100 → 3 × 1.100 = 3.300 (eski: 3.960).
/// - Mod da değiştirilirse kullanıcının seçtiği modun anlamı geçerli: "Günlük"e çekip 1.000 → 3 × 1.200 = 3.600.
/// </summary>
[Collection("postgres")]
public sealed class RezervasyonUcretDuzenlemeTests(PostgresFixture fx)
{
    private static BookingInput Input(Guid customer, Guid vehicle, decimal fee, string mode) => new()
    {
        MusteriId = customer, VehicleId = vehicle, BasTar = TestZaman.DaysLater(5), BitTar = TestZaman.DaysLater(8),
        GunlukUcret = fee, FiyatTuru = mode,
    };

    private async Task<(decimal Before, decimal After, string? Mode)> EditAsync(
        decimal fee, string mode, decimal newFee, string? newMode = null)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customer = await TestCustomer.NewAsync(sp);
        var vehicle = await TestVehicle.NewAsync(sp);
        var reservations = sp.GetRequiredService<ReservationService>();

        var id = await reservations.CreateAsync(Input(customer, vehicle, fee, mode));
        var before = (await reservations.GetAsync(id))!;
        var edit = Input(customer, vehicle, newFee, newMode ?? before.FiyatTuru!);
        edit.BasTar = before.BasTar;
        edit.BitTar = before.BitTar;
        Assert.True(await reservations.UpdateAsync(id, edit));
        var after = (await reservations.GetAsync(id))!;
        return (before.Tutar, after.Tutar, after.FiyatTuru);
    }

    [Fact]
    public async Task Toplam_modda_ucret_duzenlemesi_gunluk_brut()
    {
        var (before, after, mode) = await EditAsync(4500m, "Toplam", 1700m);
        Assert.Equal(5400m, before);
        Assert.Equal(5100m, after);
        Assert.Equal("KDV Dahil Günlük", mode);
    }

    [Fact]
    public async Task KDV_dahil_toplam_modda_ucret_duzenlemesi_gunluk_brut()
    {
        var (before, after, mode) = await EditAsync(5400m, "KDV Dahil Toplam", 1700m);
        Assert.Equal(5400m, before);
        Assert.Equal(5100m, after);
        Assert.Equal("KDV Dahil Günlük", mode);
    }

    [Fact]
    public async Task Gunluk_net_modda_ucret_duzenlemesi_gunluk_brut()
    {
        var (before, after, mode) = await EditAsync(1000m, "Günlük", 1100m);
        Assert.Equal(3600m, before);
        Assert.Equal(3300m, after);
        Assert.Equal("KDV Dahil Günlük", mode);
    }

    [Fact]
    public async Task Mod_da_degistirilirse_secilen_modun_anlami_gecerli()
    {
        var (before, after, mode) = await EditAsync(5400m, "KDV Dahil Toplam", 1000m, newMode: "Günlük");
        Assert.Equal(5400m, before);
        Assert.Equal(3600m, after);
        Assert.Equal("Günlük", mode);
    }
}
