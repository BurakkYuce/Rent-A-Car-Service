using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.PaymentTypes;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ödeme tipi master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon. (Entity PaymentType — CashTransactionType enum'undan ayrı.)
/// </summary>
[Collection("postgres")]
public sealed class PaymentTypeTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PaymentTypeService>();

        var id = await svc.CreateAsync(new PaymentTypeInput { Kod = "nakit", Ad = "Nakit" });
        var got = await svc.GetAsync(id);
        Assert.Equal("NAKIT", got!.Kod);
        Assert.Equal("Nakit", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PaymentTypeService>();

        await svc.CreateAsync(new PaymentTypeInput { Kod = "KK", Ad = "Kredi Kartı" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new PaymentTypeInput { Kod = "kk", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PaymentTypeService>();

        var a = await svc.CreateAsync(new PaymentTypeInput { Kod = "A", Ad = "A Tip" });
        await svc.CreateAsync(new PaymentTypeInput { Kod = "B", Ad = "B Tip" });
        await svc.UpdateAsync(a, new PaymentTypeInput { Kod = "A", Ad = "A Tip", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<PaymentTypeService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new PaymentTypeInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new PaymentTypeInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<PaymentTypeService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new PaymentTypeInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task PaymentTypes_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<PaymentTypeService>()
                .CreateAsync(new PaymentTypeInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<PaymentTypeService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new PaymentTypeInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
