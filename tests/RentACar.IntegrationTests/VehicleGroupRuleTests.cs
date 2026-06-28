using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç grubu fiyat-kural alanları — bağımsız oracle. SIPP/segment/kasa, koltuk/kapı/bagaj,
/// sürücü yaşı/ehliyet yılı, provizyon/muafiyet, günlük KM + aşım ücreti roundtrip + doğrulama.
/// </summary>
[Collection("postgres")]
public sealed class VehicleGroupRuleTests(PostgresFixture fx)
{
    [Fact]
    public async Task Rule_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await svc.CreateAsync(new VehicleGroupInput
        {
            Kod = "suv", Ad = "SUV", Sipp = "ifar", Segment = "Üst", KasaTuru = "SUV",
            KoltukSayisi = 5, KapiSayisi = 5, BagajSayisi = 2,
            SurucuMinYas = 25, GencSurucuYas = 27, EhliyetMinYil = 3,
            Provizyon = 7500.00m, MuafiyetTutari = 3000.50m, GunlukKmLimiti = 300, AsimKmUcreti = 4.25m
        });

        var g = await svc.GetAsync(id);
        Assert.NotNull(g);
        Assert.Equal("IFAR", g!.Sipp);          // SIPP büyük harfe normalize
        Assert.Equal("Üst", g.Segment);
        Assert.Equal("SUV", g.KasaTuru);
        Assert.Equal(5, g.KoltukSayisi);
        Assert.Equal(2, g.BagajSayisi);
        Assert.Equal(25, g.SurucuMinYas);
        Assert.Equal(27, g.GencSurucuYas);
        Assert.Equal(3, g.EhliyetMinYil);
        Assert.Equal(7500.00m, g.Provizyon);
        Assert.Equal(3000.50m, g.MuafiyetTutari);
        Assert.Equal(300, g.GunlukKmLimiti);
        Assert.Equal(4.25m, g.AsimKmUcreti);
    }

    [Fact]
    public async Task Rule_fields_optional_simple_dictionary_still_works()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        // Yalnız Kod/Ad — basit sözlük olarak kullanım korunur.
        var id = await svc.CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var g = await svc.GetAsync(id);
        Assert.Null(g!.Sipp);
        Assert.Null(g.Provizyon);
        Assert.Null(g.GunlukKmLimiti);
        Assert.Null(g.SurucuMinYas);
    }

    [Fact]
    public async Task Negative_provision_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "X", Ad = "X", Provizyon = -1m }));
    }

    [Fact]
    public async Task Invalid_sipp_length_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "Y", Ad = "Y", Sipp = "AB" }));
    }

    [Fact]
    public async Task Out_of_range_driver_age_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "Z", Ad = "Z", SurucuMinYas = 12 }));
    }
}
