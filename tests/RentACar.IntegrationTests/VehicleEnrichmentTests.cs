using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç kimlik + filo status zenginleştirme — bağımsız oracle. Yeni additive alanların
/// (Tip/Segment/Sipp/Renk/ModelYili/Vites/SasiNo/MotorNo/FiloDurum) roundtrip'i + doğrulama.
/// </summary>
[Collection("postgres")]
public sealed class VehicleEnrichmentTests(PostgresFixture fx)
{
    [Fact]
    public async Task New_identity_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput
        {
            Plaka = "34 ABC 01", Marka = "Fiat", Tip = "Egea", Segment = "Ekonomik",
            Sipp = "cdmd", Renk = "Beyaz", ModelYili = 2022, Vites = Vites.Manuel,
            SasiNo = "NM4", MotorNo = "M55", Durum = VehicleStatus.Musait,
            FiloDurum = FiloStatus.Havuz, Km = 100, Yakit = FuelType.Dizel
        });

        var v = await svc.GetAsync(id);
        Assert.NotNull(v);
        Assert.Equal("Egea", v!.Tip);
        Assert.Equal("Ekonomik", v.Segment);
        Assert.Equal("CDMD", v.Sipp);           // SIPP büyük harfe normalize
        Assert.Equal("Beyaz", v.Renk);
        Assert.Equal(2022, v.ModelYili);
        Assert.Equal(Vites.Manuel, v.Vites);
        Assert.Equal("NM4", v.SasiNo);
        Assert.Equal("M55", v.MotorNo);
        Assert.Equal(FiloStatus.Havuz, v.FiloDurum);
        Assert.Equal(VehicleStatus.Musait, v.Durum); // filo status operasyonel durumdan AYRI
    }

    [Fact]
    public async Task Optional_fields_default_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput { Plaka = "06 NUL 01" });
        var v = await svc.GetAsync(id);
        Assert.Null(v!.Tip);
        Assert.Null(v.Segment);
        Assert.Null(v.Sipp);
        Assert.Null(v.ModelYili);
        Assert.Null(v.Vites);
        Assert.Null(v.FiloDurum);
    }

    [Fact]
    public async Task Update_changes_filo_status_independently()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput
        { Plaka = "35 FLO 01", Durum = VehicleStatus.Musait, FiloDurum = FiloStatus.SifirKmStok });
        await svc.UpdateAsync(id, new VehicleInput
        { Plaka = "35 FLO 01", Durum = VehicleStatus.Musait, FiloDurum = FiloStatus.IkinciElSatis });

        var v = await svc.GetAsync(id);
        Assert.Equal(FiloStatus.IkinciElSatis, v!.FiloDurum);
        Assert.Equal(VehicleStatus.Musait, v.Durum); // operasyonel durum değişmedi
    }

    [Fact]
    public async Task Invalid_sipp_length_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleInput { Plaka = "34 SIP 01", Sipp = "CDM" }));
    }

    [Fact]
    public async Task Out_of_range_model_year_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new VehicleInput { Plaka = "34 YIL 01", ModelYili = 1900 }));
    }
}
