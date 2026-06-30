using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Locations;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap K3 — Branch + Location derinlik (additive). BAĞIMSIZ ORACLE: derinlik alanları Create üzerinden
/// kalıcılaşıp Get ile geri okunur (Normalize/Apply eşlemesi). VehicleGroup KM-kademe zaten mevcuttu → kapsam dışı.
/// </summary>
[Collection("postgres")]
public sealed class SubeLokasyonDerinlikTests(PostgresFixture fx)
{
    [Fact]
    public async Task Branch_derinlik_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();

        var id = await svc.CreateAsync(new BranchInput
        {
            Kod = "merkez", Ad = "Merkez Ofis", Eposta = "merkez@firma.com", Il = "İstanbul", Ilce = "Kadıköy",
            Yetkili = "Ali Veli", CalismaSaatleri = "09:00-18:00", KomisyonOran = 0.10m, EvrakNoOnek = "MRK-"
        });

        var b = await svc.GetAsync(id);
        Assert.Equal("MERKEZ", b!.Kod);                 // büyük harfe normalize
        Assert.Equal("merkez@firma.com", b.Eposta);
        Assert.Equal("İstanbul", b.Il);
        Assert.Equal("Kadıköy", b.Ilce);
        Assert.Equal("Ali Veli", b.Yetkili);
        Assert.Equal("09:00-18:00", b.CalismaSaatleri);
        Assert.Equal(0.10m, b.KomisyonOran);
        Assert.Equal("MRK-", b.EvrakNoOnek);
    }

    [Fact]
    public async Task Location_derinlik_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput
        {
            Kod = "ist-hvl", Ad = "İstanbul Havalimanı", Eposta = "hvl@firma.com",
            CalismaSaatleri = "07:00-23:00", TeslimUcreti = 250.50m
        });

        var l = await svc.GetAsync(id);
        Assert.Equal("IST-HVL", l!.Kod);
        Assert.Equal("hvl@firma.com", l.Eposta);
        Assert.Equal("07:00-23:00", l.CalismaSaatleri);
        Assert.Equal(250.50m, l.TeslimUcreti);
    }
}
