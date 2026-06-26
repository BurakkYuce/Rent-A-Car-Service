using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class CustomerTests(PostgresFixture fx)
{
    private const string ValidTc = "10000000146";
    private const string ValidTc2 = "12345678950";

    [Fact]
    public async Task Individual_requires_ad()
    {
        var svc = Svc(out _);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "  " }));
    }

    [Fact]
    public async Task Individual_invalid_tc_rejected()
    {
        var svc = Svc(out _);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Ali", TcKimlik = "11111111111" }));
    }

    [Fact]
    public async Task Corporate_requires_unvan()
    {
        var svc = Svc(out _);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "" }));
    }

    [Fact]
    public async Task Corporate_bad_vergino_rejected()
    {
        var svc = Svc(out _);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "ACME A.Ş.", VergiNo = "123" }));
    }

    [Fact]
    public async Task Create_read_roundtrips()
    {
        var svc = Svc(out _);
        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Bireysel, Ad = "Ayşe", Soyad = "Yılmaz", TcKimlik = ValidTc, Email = "ayse@example.com"
        });
        var c = await svc.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal("Ayşe Yılmaz", c!.DisplayName);
        Assert.Equal(ValidTc, c.TcKimlik);
    }

    [Fact]
    public async Task Duplicate_tc_same_tenant_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        await svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "A", TcKimlik = ValidTc });
        await Assert.ThrowsAsync<DuplicateCariException>(
            () => svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "B", TcKimlik = ValidTc }));
    }

    [Fact]
    public async Task Same_tc_different_tenants_allowed()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "A", TcKimlik = ValidTc });

        using (var s2 = host.ScopeFor(t2))
        {
            var id = await s2.ServiceProvider.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "B", TcKimlik = ValidTc });
            Assert.NotEqual(Guid.Empty, id);
        }
    }

    [Fact]
    public async Task Customers_are_tenant_isolated_ef_and_rls()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "T1", TcKimlik = ValidTc });
        using (var s2 = host.ScopeFor(t2))
            await s2.ServiceProvider.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "T2", TcKimlik = ValidTc2 });

        using var scope = host.ScopeFor(t2);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // EF filter
        var viaLinq = await db.Customers.Select(c => c.Ad).ToListAsync();
        Assert.Equal(["T2"], viaLinq);

        // RLS (EF filter atlanmış raw)
        var rawCount = await db.Database
            .SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"Customers\"")
            .SingleAsync();
        Assert.Equal(1, rawCount);
    }

    [Fact]
    public async Task Create_writes_audit_row()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "auditor");
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Denetim", TcKimlik = ValidTc });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal("Customers", log.EntityName);
        Assert.Equal(id.ToString(), log.EntityId);
        Assert.Equal("auditor", log.UserName);
        Assert.Contains("Denetim", log.NewValues);
    }

    private CustomerService Svc(out TestHost host)
    {
        host = new TestHost(fx.AppConnectionString);
        var scope = host.ScopeFor(Guid.NewGuid());
        return scope.ServiceProvider.GetRequiredService<CustomerService>();
    }
}
