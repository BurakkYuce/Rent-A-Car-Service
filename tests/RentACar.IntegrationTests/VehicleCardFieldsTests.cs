using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G1 — araç kartı ek alanlar (additive). BAĞIMSIZ ORACLE: yeni alanlar (HGS/OGS, kasa/detay tipi,
/// alış fatura/firma, km limiti) roundtrip; opsiyonel (boş → null).
/// </summary>
[Collection("postgres")]
public sealed class VehicleCardFieldsTests(PostgresFixture fx)
{
    [Fact]
    public async Task New_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput
        {
            Plaka = "34 GK 01",
            HgsNo = "HGS-123", OgsNo = "OGS-456", KasaTipi = "Sedan", DetayTipi = "SUV",
            AlimFaturaNo = "FT-2026-99", AlimYapilanFirma = "Bayi A.Ş.", KiraKmLimiti = 3000
        });

        var v = await svc.GetAsync(id);
        Assert.Equal("HGS-123", v!.HgsNo);
        Assert.Equal("OGS-456", v.OgsNo);
        Assert.Equal("Sedan", v.KasaTipi);
        Assert.Equal("SUV", v.DetayTipi);
        Assert.Equal("FT-2026-99", v.AlimFaturaNo);
        Assert.Equal("Bayi A.Ş.", v.AlimYapilanFirma);
        Assert.Equal(3000, v.KiraKmLimiti);
    }

    [Fact]
    public async Task New_fields_optional_default_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput { Plaka = "34 GK 02" });
        var v = await svc.GetAsync(id);
        Assert.Null(v!.HgsNo);
        Assert.Null(v.KasaTipi);
        Assert.Null(v.KiraKmLimiti);
    }
}
