using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.PenaltyTypes;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ceza türü master — bağımsız oracle. CRUD + kod normalize/benzersizlik + VarsayilanTutar
/// opsiyonel/≥0 + aktif filtre (dropdown kaynağı) + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class PenaltyTypeTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        var id = await svc.CreateAsync(new PenaltyTypeInput { Kod = "hiz", Ad = "Hız İhlali", VarsayilanTutar = 1500.50m });
        var got = await svc.GetAsync(id);
        Assert.Equal("HIZ", got!.Kod);
        Assert.Equal("Hız İhlali", got.Ad);
        Assert.Equal(1500.50m, got.VarsayilanTutar);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task VarsayilanTutar_optional_null_allowed()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        var id = await svc.CreateAsync(new PenaltyTypeInput { Kod = "PARK", Ad = "Park İhlali" });
        Assert.Null((await svc.GetAsync(id))!.VarsayilanTutar);
    }

    [Fact]
    public async Task Negative_varsayilan_tutar_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new PenaltyTypeInput { Kod = "X", Ad = "Negatif", VarsayilanTutar = -10m }));
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        await svc.CreateAsync(new PenaltyTypeInput { Kod = "HGS", Ad = "HGS İhlali" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new PenaltyTypeInput { Kod = "hgs", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        var a = await svc.CreateAsync(new PenaltyTypeInput { Kod = "A", Ad = "A Tür" });
        await svc.CreateAsync(new PenaltyTypeInput { Kod = "B", Ad = "B Tür" });
        await svc.UpdateAsync(a, new PenaltyTypeInput { Kod = "A", Ad = "A Tür", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new PenaltyTypeInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new PenaltyTypeInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task Delete_removes_type()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();

        var id = await svc.CreateAsync(new PenaltyTypeInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyTypeService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new PenaltyTypeInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task PenaltyTypes_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<PenaltyTypeService>()
                .CreateAsync(new PenaltyTypeInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<PenaltyTypeService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new PenaltyTypeInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
