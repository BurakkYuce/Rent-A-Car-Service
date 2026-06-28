using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.InsuranceCompanies;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Sigorta şirketi master — bağımsız oracle. CRUD + kod normalize/benzersizlik + Telefon roundtrip +
/// aktif filtre + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class InsuranceCompanyTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<InsuranceCompanyService>();

        var id = await svc.CreateAsync(new InsuranceCompanyInput { Kod = "allianz", Ad = "Allianz Sigorta", Telefon = "0850 000" });
        var got = await svc.GetAsync(id);
        Assert.Equal("ALLIANZ", got!.Kod);
        Assert.Equal("Allianz Sigorta", got.Ad);
        Assert.Equal("0850 000", got.Telefon);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<InsuranceCompanyService>();

        await svc.CreateAsync(new InsuranceCompanyInput { Kod = "AXA", Ad = "Axa" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new InsuranceCompanyInput { Kod = "axa", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<InsuranceCompanyService>();

        var a = await svc.CreateAsync(new InsuranceCompanyInput { Kod = "A", Ad = "A Sigorta" });
        await svc.CreateAsync(new InsuranceCompanyInput { Kod = "B", Ad = "B Sigorta" });
        await svc.UpdateAsync(a, new InsuranceCompanyInput { Kod = "A", Ad = "A Sigorta", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<InsuranceCompanyService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new InsuranceCompanyInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new InsuranceCompanyInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<InsuranceCompanyService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new InsuranceCompanyInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Companies_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<InsuranceCompanyService>()
                .CreateAsync(new InsuranceCompanyInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<InsuranceCompanyService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new InsuranceCompanyInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
