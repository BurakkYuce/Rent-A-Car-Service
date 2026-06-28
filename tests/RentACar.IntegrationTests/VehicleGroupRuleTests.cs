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
    public async Task Extended_rule_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        // Canlı parite ek alanları (docs/parite/02). Beklenenler senaryodan.
        var id = await svc.CreateAsync(new VehicleGroupInput
        {
            Kod = "lux", Ad = "Lüks", Marka = "BMW", Tipi = "5.20i",
            KucukBagaj = 2, BuyukBagaj = 3, GencEhliyetMinYil = 5,
            Provizyon2 = 12000.00m, Muafiyet2 = 5000.75m, AylikMaxKm = 4500,
            YakitFiyati = 44.90m, SonraOdeOran = 35.00m, KrediKartiSart = true,
            WebSira = 7, UpgradeSira = 2
        });

        var g = await svc.GetAsync(id);
        Assert.NotNull(g);
        Assert.Equal("BMW", g!.Marka);
        Assert.Equal("5.20i", g.Tipi);
        Assert.Equal(2, g.KucukBagaj);
        Assert.Equal(3, g.BuyukBagaj);
        Assert.Equal(5, g.GencEhliyetMinYil);
        Assert.Equal(12000.00m, g.Provizyon2);
        Assert.Equal(5000.75m, g.Muafiyet2);
        Assert.Equal(4500, g.AylikMaxKm);
        Assert.Equal(44.90m, g.YakitFiyati);
        Assert.Equal(35.00m, g.SonraOdeOran);
        Assert.True(g.KrediKartiSart);
        Assert.Equal(7, g.WebSira);
        Assert.Equal(2, g.UpgradeSira);
    }

    [Fact]
    public async Task SonraOde_oran_out_of_range_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "P", Ad = "P", SonraOdeOran = 150m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleGroupInput { Kod = "P2", Ad = "P2", Provizyon2 = -1m }));
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
