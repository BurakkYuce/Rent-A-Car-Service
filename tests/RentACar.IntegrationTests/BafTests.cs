using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap L5 — BAF personel araç tahsis. BAĞIMSIZ ORACLE: No "BAF-"; çıkış 10000 → teslim 10500 (Kapandi);
/// dönüş km &lt; çıkış reddedilir; tenant izolasyonu (RLS).
/// </summary>
[Collection("postgres")]
public sealed class BafTests(PostgresFixture fx)
{
    [Fact]
    public async Task Tahsis_ve_teslim_al()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BafService>();

        var id = await svc.CreateAsync(new BafInput
        { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 10_000, Sube = "Merkez" });

        var b = await svc.GetAsync(id);
        Assert.StartsWith("BAF-", b!.No);
        Assert.Equal(BafDurum.Acik, b.Durum);

        Assert.True(await svc.TeslimAlAsync(id, donusKm: 10_500, donusYakit: 50));
        var b2 = await svc.GetAsync(id);
        Assert.Equal(BafDurum.Kapandi, b2!.Durum);
        Assert.Equal(10_500, b2.DonusKm);   // çıkış 10000 → dönüş 10500 (elle oracle)
    }

    [Fact]
    public async Task Donus_km_kucuk_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BafService>();
        var id = await svc.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 10_000 });
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => svc.TeslimAlAsync(id, donusKm: 9_000, donusYakit: null));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<BafService>()
                .CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 100 });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<BafService>().ListAsync());
    }
}
