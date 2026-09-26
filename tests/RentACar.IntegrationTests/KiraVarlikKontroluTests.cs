using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// DEVIR §6 Low (harici RentalsApi): müşteri/araç varlık kontrolü artık <see cref="RentalService.CreateDirectAsync"/>
/// GİRİŞİNDE — harici API, /api/ui ve Blazor formu aynı kuraldan geçer. Olmayan ya da BAŞKA KİRACININ kimliği
/// <see cref="ValidationException"/> (Alan dolu) ile reddedilir ve hiçbir kira yazılmaz (racar_app ile okunur).
/// Uç düzeyi 400 kilidi <c>LowTemizligiBTests.Harici_api_yabanci_ya_da_olmayan_musteri_arac_400_ve_kayit_yazilmaz</c>.
/// </summary>
[Collection("postgres")]
public sealed class KiraVarlikKontroluTests(PostgresFixture fx)
{
    private static DateTimeOffset Base(int daysLater)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(daysLater), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static BookingInput Input(Guid customer, Guid vehicle) => new()
    {
        MusteriId = customer, VehicleId = vehicle, BasTar = Base(1), BitTar = Base(4), GunlukUcret = 100m
    };

    private static async Task<(Guid Cari, Guid Arac)> RecordsAsync(TestHost host, Guid tenant, string plate)
    {
        using var s = host.ScopeFor(tenant);
        var account = await s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Varlik", Soyad = "Test" });
        var vehicle = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate });
        return (account, vehicle);
    }

    private static async Task<int> RentalCountAsync(TestHost host, Guid tenant)
    {
        using var s = host.ScopeFor(tenant);
        return (await s.ServiceProvider.GetRequiredService<RentalService>().ListAsync()).Count;
    }

    [Fact]
    public async Task Olmayan_ya_da_yabanci_musteri_arac_reddedilir_kira_yazilmaz_gecerli_kimlik_acilir()
    {
        using var host = new TestHost(fx.AppConnectionString); // racar_app — RLS + tenant filtresi etkin
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var suffix = Random.Shared.Next(100, 999);
        var (aAccount, aVehicle) = await RecordsAsync(host, tenantA, $"34VKA{suffix}");
        var (bAccount, bVehicle) = await RecordsAsync(host, tenantB, $"34VKB{suffix}");

        using (var s = host.ScopeFor(tenantA))
        {
            var rentals = s.ServiceProvider.GetRequiredService<RentalService>();

            var ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(Input(Guid.NewGuid(), aVehicle)));
            Assert.Equal("musteriId", ex.Alan);                                                  // olmayan cari
            ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(Input(bAccount, aVehicle)));
            Assert.Equal("musteriId", ex.Alan);                                                  // B'nin carisi
            ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(Input(aAccount, Guid.NewGuid())));
            Assert.Equal("vehicleId", ex.Alan);                                                  // olmayan araç
            ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(Input(aAccount, bVehicle)));
            Assert.Equal("vehicleId", ex.Alan);                                                  // B'nin aracı
        }

        // Hiçbir kira yazılmadı — ne A'da ne B'de (racar_app ile okunur).
        Assert.Equal(0, await RentalCountAsync(host, tenantA));
        Assert.Equal(0, await RentalCountAsync(host, tenantB));

        // Kendi kiracısının kayıtlarıyla meşru yol hâlâ açılır: 3 gün × 100 = 300 (elle kurulan senaryo).
        using (var s = host.ScopeFor(tenantA))
        {
            var rentals = s.ServiceProvider.GetRequiredService<RentalService>();
            var id = await rentals.CreateDirectAsync(Input(aAccount, aVehicle));
            var rental = await rentals.GetAsync(id);
            Assert.NotNull(rental);
            Assert.Equal(aAccount, rental!.MusteriId);
            Assert.Equal(aVehicle, rental.VehicleId);
            Assert.Equal(300m, rental.Tutar);
        }
        Assert.Equal(1, await RentalCountAsync(host, tenantA));
        Assert.Equal(0, await RentalCountAsync(host, tenantB));
    }
}
