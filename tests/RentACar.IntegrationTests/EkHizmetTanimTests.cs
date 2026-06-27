using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ek hizmet tanımı master — bağımsız oracle. CRUD + kod normalize/benzersizlik + KDV doğrulama +
/// aktif filtre (kira formu kaynağı) + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class EkHizmetTanimTests(PostgresFixture fx)
{
    private static EkHizmetTanimInput In(string kod, string ad, decimal birim = 100m, decimal kdv = 0.20m, bool aktif = true)
        => new() { Kod = kod, Ad = ad, BirimUcret = birim, KdvOrani = kdv, Aktif = aktif };

    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        var id = await svc.CreateAsync(In("gps", "Navigasyon", 50m, 0.20m));
        var got = await svc.GetAsync(id);
        Assert.Equal("GPS", got!.Kod);
        Assert.Equal("Navigasyon", got.Ad);
        Assert.Equal(50m, got.BirimUcret);
        Assert.Equal(0.20m, got.KdvOrani);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        await svc.CreateAsync(In("BEBEK", "Bebek Koltuğu"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(In("bebek", "Tekrar")));
    }

    [Fact]
    public async Task Validation_rejects_bad_kdv_and_required_fields()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(In("", "Ad")));            // kod yok
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(In("X", "")));             // ad yok
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(In("X", "A", -1m)));       // negatif ücret
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(In("X", "A", 10m, 1.5m))); // kdv > 1
    }

    [Fact]
    public async Task ListActive_excludes_passive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        var a = await svc.CreateAsync(In("A", "A hizmet"));
        await svc.CreateAsync(In("B", "B hizmet"));
        await svc.UpdateAsync(a, In("A", "A hizmet", aktif: false));

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<EkHizmetTanimService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(In("X", "Yetkisiz")));
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<EkHizmetTanimService>().CreateAsync(In("T1", "Tenant1"));
        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<EkHizmetTanimService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(In("T1", "Tenant2")); // aynı kod farklı tenant'ta serbest
        Assert.Single(await svc2.ListAsync());
    }
}
