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
    private static DateTimeOffset Taban(int gunSonra)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(gunSonra), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static BookingInput Girdi(Guid musteri, Guid arac) => new()
    {
        MusteriId = musteri, VehicleId = arac, BasTar = Taban(1), BitTar = Taban(4), GunlukUcret = 100m
    };

    private static async Task<(Guid Cari, Guid Arac)> KayitlarAsync(TestHost host, Guid tenant, string plaka)
    {
        using var s = host.ScopeFor(tenant);
        var cari = await s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Varlik", Soyad = "Test" });
        var arac = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka });
        return (cari, arac);
    }

    private static async Task<int> KiraSayisiAsync(TestHost host, Guid tenant)
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
        var sonek = Random.Shared.Next(100, 999);
        var (aCari, aArac) = await KayitlarAsync(host, tenantA, $"34VKA{sonek}");
        var (bCari, bArac) = await KayitlarAsync(host, tenantB, $"34VKB{sonek}");

        using (var s = host.ScopeFor(tenantA))
        {
            var kiralar = s.ServiceProvider.GetRequiredService<RentalService>();

            var ex = await Assert.ThrowsAsync<ValidationException>(() => kiralar.CreateDirectAsync(Girdi(Guid.NewGuid(), aArac)));
            Assert.Equal("musteriId", ex.Alan);                                                  // olmayan cari
            ex = await Assert.ThrowsAsync<ValidationException>(() => kiralar.CreateDirectAsync(Girdi(bCari, aArac)));
            Assert.Equal("musteriId", ex.Alan);                                                  // B'nin carisi
            ex = await Assert.ThrowsAsync<ValidationException>(() => kiralar.CreateDirectAsync(Girdi(aCari, Guid.NewGuid())));
            Assert.Equal("vehicleId", ex.Alan);                                                  // olmayan araç
            ex = await Assert.ThrowsAsync<ValidationException>(() => kiralar.CreateDirectAsync(Girdi(aCari, bArac)));
            Assert.Equal("vehicleId", ex.Alan);                                                  // B'nin aracı
        }

        // Hiçbir kira yazılmadı — ne A'da ne B'de (racar_app ile okunur).
        Assert.Equal(0, await KiraSayisiAsync(host, tenantA));
        Assert.Equal(0, await KiraSayisiAsync(host, tenantB));

        // Kendi kiracısının kayıtlarıyla meşru yol hâlâ açılır: 3 gün × 100 = 300 (elle kurulan senaryo).
        using (var s = host.ScopeFor(tenantA))
        {
            var kiralar = s.ServiceProvider.GetRequiredService<RentalService>();
            var id = await kiralar.CreateDirectAsync(Girdi(aCari, aArac));
            var kira = await kiralar.GetAsync(id);
            Assert.NotNull(kira);
            Assert.Equal(aCari, kira!.MusteriId);
            Assert.Equal(aArac, kira.VehicleId);
            Assert.Equal(300m, kira.Tutar);
        }
        Assert.Equal(1, await KiraSayisiAsync(host, tenantA));
        Assert.Equal(0, await KiraSayisiAsync(host, tenantB));
    }
}
