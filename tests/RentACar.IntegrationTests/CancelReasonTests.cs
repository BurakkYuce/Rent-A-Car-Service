using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.CancelReasons;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// İptal sebebi master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class CancelReasonTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CancelReasonService>();

        var id = await svc.CreateAsync(new CancelReasonInput { Kod = "vazgecti", Ad = "Müşteri Vazgeçti" });
        var got = await svc.GetAsync(id);
        Assert.Equal("VAZGECTI", got!.Kod);
        Assert.Equal("Müşteri Vazgeçti", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CancelReasonService>();

        await svc.CreateAsync(new CancelReasonInput { Kod = "ARIZA", Ad = "Araç Arızası" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CancelReasonInput { Kod = "ariza", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CancelReasonService>();

        var a = await svc.CreateAsync(new CancelReasonInput { Kod = "A", Ad = "A Sebep" });
        await svc.CreateAsync(new CancelReasonInput { Kod = "B", Ad = "B Sebep" });
        await svc.UpdateAsync(a, new CancelReasonInput { Kod = "A", Ad = "A Sebep", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CancelReasonService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CancelReasonInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CancelReasonInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<CancelReasonService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CancelReasonInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task CancelReasons_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CancelReasonService>()
                .CreateAsync(new CancelReasonInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<CancelReasonService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new CancelReasonInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
