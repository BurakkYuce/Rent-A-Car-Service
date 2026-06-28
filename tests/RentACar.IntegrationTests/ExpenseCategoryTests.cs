using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.ExpenseCategories;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Gider türü master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon. (Entity adı ExpenseCategory — Domain.Enums.ExpenseType ile çakışmasın.)
/// </summary>
[Collection("postgres")]
public sealed class ExpenseCategoryTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var id = await svc.CreateAsync(new ExpenseCategoryInput { Kod = "yakit", Ad = "Yakıt" });
        var got = await svc.GetAsync(id);
        Assert.Equal("YAKIT", got!.Kod);
        Assert.Equal("Yakıt", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        await svc.CreateAsync(new ExpenseCategoryInput { Kod = "BAKIM", Ad = "Bakım" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new ExpenseCategoryInput { Kod = "bakim", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var a = await svc.CreateAsync(new ExpenseCategoryInput { Kod = "A", Ad = "A Tür" });
        await svc.CreateAsync(new ExpenseCategoryInput { Kod = "B", Ad = "B Tür" });
        await svc.UpdateAsync(a, new ExpenseCategoryInput { Kod = "A", Ad = "A Tür", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ExpenseCategoryInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ExpenseCategoryInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new ExpenseCategoryInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Categories_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<ExpenseCategoryService>()
                .CreateAsync(new ExpenseCategoryInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<ExpenseCategoryService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new ExpenseCategoryInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
