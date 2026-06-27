using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Auditing;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class AuditViewTests(PostgresFixture fx)
{
    [Fact]
    public async Task Audit_search_returns_actions_and_filters()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "denetciadmin", UserRole.Admin);

        // Bilinen işlemler: araç oluştur (Create) + güncelle (Update), cari oluştur.
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var vid = await vehicles.CreateAsync(new VehicleInput { Plaka = "34AUD01" });
        await vehicles.UpdateAsync(vid, new VehicleInput { Plaka = "34AUD01", Marka = "Güncel" });
        await customers.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Denet", Soyad = "Test" });

        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

        var all = await audit.SearchAsync(new AuditFilter());
        Assert.True(all.Total >= 3);

        // Entity filtresi: yalnız Vehicles (Create + Update = 2).
        var veh = await audit.SearchAsync(new AuditFilter { EntityName = "Vehicles" });
        Assert.Equal(2, veh.Total);
        Assert.All(veh.Items, a => Assert.Equal("Vehicles", a.EntityName));

        // Aksiyon filtresi: yalnız Create (araç + cari = 2).
        var creates = await audit.SearchAsync(new AuditFilter { Action = AuditAction.Create });
        Assert.True(creates.Items.All(a => a.Action == AuditAction.Create));
        Assert.Contains(creates.Items, a => a.EntityName == "Customers");

        // Kullanıcı + en yeni önce: ilk kayıt bizim kullanıcı.
        var byUser = await audit.SearchAsync(new AuditFilter { UserName = "denetci" });
        Assert.True(byUser.Total >= 3);
        Assert.Equal("denetciadmin", byUser.Items[0].UserName);
    }

    [Fact]
    public async Task NonAdmin_cannot_view_audit()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        await Assert.ThrowsAsync<ValidationException>(() => audit.SearchAsync(new AuditFilter()));
    }

    [Fact]
    public async Task Audit_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1, Guid.NewGuid(), "a1", UserRole.Admin))
            await s1.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34T1AU" });

        using var s2 = host.ScopeFor(t2, Guid.NewGuid(), "a2", UserRole.Admin);
        var page = await s2.ServiceProvider.GetRequiredService<AuditService>().SearchAsync(new AuditFilter());
        Assert.Equal(0, page.Total); // t2 yalnız kendi denetim kayıtlarını görür
    }
}
