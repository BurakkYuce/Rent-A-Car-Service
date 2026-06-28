using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.KdvRates;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// KDV oranı master — bağımsız oracle. CRUD + kod normalize/benzersizlik + 0..1 doğrulama +
/// aktif filtre (dropdown kaynağı) + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class KdvRateTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();

        var id = await svc.CreateAsync(new KdvRateInput { Kod = "kdv20", Ad = "Genel %20", Oran = 0.20m });
        var got = await svc.GetAsync(id);
        Assert.Equal("KDV20", got!.Kod);
        Assert.Equal("Genel %20", got.Ad);
        Assert.Equal(0.20m, got.Oran);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();

        await svc.CreateAsync(new KdvRateInput { Kod = "KDV10", Ad = "İndirimli %10", Oran = 0.10m });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new KdvRateInput { Kod = "kdv10", Ad = "Başka", Oran = 0.01m }));
    }

    [Fact]
    public async Task Oran_out_of_range_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new KdvRateInput { Kod = "X", Ad = "Yüksek", Oran = 1.5m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new KdvRateInput { Kod = "Y", Ad = "Negatif", Oran = -0.1m }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();

        var a = await svc.CreateAsync(new KdvRateInput { Kod = "A", Ad = "A", Oran = 0.20m });
        await svc.CreateAsync(new KdvRateInput { Kod = "B", Ad = "B", Oran = 0.10m });
        await svc.UpdateAsync(a, new KdvRateInput { Kod = "A", Ad = "A", Oran = 0.20m, Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count); // pasif korunur
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new KdvRateInput { Kod = "", Ad = "Ad", Oran = 0.20m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new KdvRateInput { Kod = "X", Ad = "  ", Oran = 0.20m }));
    }

    [Fact]
    public async Task Delete_removes_rate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();

        var id = await svc.CreateAsync(new KdvRateInput { Kod = "DEL", Ad = "Silinecek", Oran = 0.20m });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<KdvRateService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new KdvRateInput { Kod = "X", Ad = "Yetkisiz", Oran = 0.20m }));
    }

    [Fact]
    public async Task KdvRates_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<KdvRateService>()
                .CreateAsync(new KdvRateInput { Kod = "T1", Ad = "Tenant1", Oran = 0.20m });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<KdvRateService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new KdvRateInput { Kod = "T1", Ad = "Tenant2", Oran = 0.10m });
        Assert.Single(await svc2.ListAsync());
    }
}
