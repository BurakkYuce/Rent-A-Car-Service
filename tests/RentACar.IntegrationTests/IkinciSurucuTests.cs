using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR3b — 2. sürücü (opsiyonel Customer bağı; sözleşmede decrypt'li gösterilir). Aynı-tenant doğrulama;
/// null → tek sürücü. PII yeni kolon YOK (Customer bağı, CustomerRepository.Decrypt yeniden kullanım).
/// </summary>
[Collection("postgres")]
public sealed class IkinciSurucuTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private static async Task<(Guid cari, Guid veh)> SeedAsync(IServiceProvider sp, string plaka, string ad)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = ad, Soyad = "S" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        return (cari, veh);
    }

    [Fact]
    public async Task Ikinci_surucu_sozlesmede_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await SeedAsync(sp, "34 IS 01", "Birinci");
        var ikinci = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Ikinci", Soyad = "Surucu", EhliyetNo = "99999", EhliyetYeri = "ANTALYA" });

        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, IkinciSurucuId = ikinci, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });

        var s = await sp.GetRequiredService<ContractService>().GetAsync(rental);
        Assert.Equal("Ikinci Surucu", s!.IkinciSurucuAd);
        Assert.Equal("99999", s.IkinciEhliyetNo);   // decrypt'li düz değer
        Assert.Equal("ANTALYA", s.IkinciEhliyetYeri);
    }

    [Fact]
    public async Task Ikinci_surucu_null_tek_surucu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await SeedAsync(sp, "34 IS 02", "Tek");
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });

        var s = await sp.GetRequiredService<ContractService>().GetAsync(rental);
        Assert.Null(s!.IkinciSurucuAd);
        Assert.Null(s.IkinciEhliyetNo);
    }

    [Fact]
    public async Task Ikinci_surucu_musteriyle_ayni_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await SeedAsync(sp, "34 IS 03", "Ayni");
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = cari, IkinciSurucuId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m }));
        Assert.Contains("aynı olamaz", ex.Message);
    }

    [Fact]
    public async Task Olmayan_ikinci_surucu_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await SeedAsync(sp, "34 IS 04", "Var");
        // Başka tenant'ın / var olmayan id → RLS zaten keser; erken temiz red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = cari, IkinciSurucuId = Guid.NewGuid(), VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m }));
        Assert.Contains("bulunamadı", ex.Message);
    }
}
