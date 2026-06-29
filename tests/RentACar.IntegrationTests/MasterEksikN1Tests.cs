using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.HesapKodlari;
using RentACar.Application.ServisTanimlari;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap N1 — HesapKodu + ServisTanim yeni mini-master'lar. BAĞIMSIZ ORACLE: roundtrip + Kod benzersizliği
/// + tenant izolasyonu (racar_app, RLS).
/// </summary>
[Collection("postgres")]
public sealed class MasterEksikN1Tests(PostgresFixture fx)
{
    [Fact]
    public async Task HesapKodu_roundtrip_ve_benzersizlik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<HesapKoduService>();

        await svc.CreateAsync(new HesapKoduInput { Kod = "600", Ad = "Yurtiçi Satışlar" });
        var list = await svc.ListAsync();
        Assert.Contains(list, h => h.Kod == "600" && h.Ad == "Yurtiçi Satışlar");

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => svc.CreateAsync(new HesapKoduInput { Kod = "600", Ad = "Çift" })); // aynı kod
    }

    [Fact]
    public async Task ServisTanim_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServisTanimService>();

        var id = await svc.CreateAsync(new ServisTanimInput { Kod = "EKO-PER", AracTipi = "Ekonomik", BakimKm = 15000 });
        var s = await svc.GetAsync(id);
        Assert.Equal("Ekonomik", s!.AracTipi);
        Assert.Equal(15000, s.BakimKm);
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<HesapKoduService>().CreateAsync(new HesapKoduInput { Kod = "X", Ad = "Gizli" });
            await a.ServiceProvider.GetRequiredService<ServisTanimService>().CreateAsync(new ServisTanimInput { Kod = "X", AracTipi = "Gizli", BakimKm = 100 });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<HesapKoduService>().ListAsync());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<ServisTanimService>().ListAsync());
    }
}
