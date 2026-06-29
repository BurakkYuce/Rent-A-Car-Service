using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.DropTanimlari;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap N2 — lokasyon-şube drop matris. BAĞIMSIZ ORACLE: roundtrip + (Lokasyon,Sube) benzersizliği
/// + tenant izolasyonu (racar_app, RLS).
/// </summary>
[Collection("postgres")]
public sealed class DropTanimTests(PostgresFixture fx)
{
    [Fact]
    public async Task Roundtrip_ve_benzersizlik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<DropTanimService>();

        var id = await svc.CreateAsync(new DropTanimInput
        { Lokasyon = "Havalimanı", Sube = "Merkez", KarsilamaSekli = "Karşılama", CalismaSekli = "7/24" });
        var d = await svc.GetAsync(id);
        Assert.Equal("Havalimanı", d!.Lokasyon);
        Assert.Equal("Merkez", d.Sube);
        Assert.Equal("7/24", d.CalismaSekli);

        // Aynı (lokasyon, şube) ikilisi → red
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => svc.CreateAsync(new DropTanimInput { Lokasyon = "Havalimanı", Sube = "Merkez" }));
        // Farklı şube → kabul
        await svc.CreateAsync(new DropTanimInput { Lokasyon = "Havalimanı", Sube = "Şube2" });
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<DropTanimService>()
                .CreateAsync(new DropTanimInput { Lokasyon = "Gizli", Sube = "Gizli" });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<DropTanimService>().ListAsync());
    }
}
