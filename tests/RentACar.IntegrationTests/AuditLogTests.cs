using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class AuditLogTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_writes_audit_row_with_who_and_new_values()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant, userId, "auditor");
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var id = await svc.CreateAsync(new VehicleInput { Plaka = "34AUD01", Marka = "Opel" });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(await db.AuditLogs.ToListAsync());

        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal("Vehicles", log.EntityName);
        Assert.Equal(id.ToString(), log.EntityId);
        Assert.Equal(tenant, log.TenantId);
        Assert.Equal(userId, log.UserId);
        Assert.Equal("auditor", log.UserName);
        Assert.Null(log.OldValues);
        Assert.Contains("34AUD01", log.NewValues);
    }

    [Fact]
    public async Task Update_writes_audit_row_with_old_and_new_diff()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "auditor");
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput { Plaka = "34UPD01", Marka = "Ford", Km = 100 });
        await svc.UpdateAsync(id, new VehicleInput { Plaka = "34UPD01", Marka = "Ford Focus", Km = 250 });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var logs = await db.AuditLogs.OrderBy(a => a.TimestampUtc).ToListAsync();

        Assert.Equal(2, logs.Count);
        var update = logs[1];
        Assert.Equal(AuditAction.Update, update.Action);
        Assert.Equal(id.ToString(), update.EntityId);
        // Yalnız değişen alanlar; eski "Ford" / yeni "Ford Focus".
        Assert.Contains("Marka", update.NewValues);
        Assert.Contains("Ford", update.OldValues);
        Assert.Contains("Ford Focus", update.NewValues);
        Assert.Contains("100", update.OldValues);
        Assert.Contains("250", update.NewValues);
    }

    [Fact]
    public async Task Audit_rows_are_tenant_scoped()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34ATEN01" });

        // T2 perspektifinden T1'in audit kayıtları görünmez (AuditLog da RLS'e tabi).
        using var s2 = host.ScopeFor(t2);
        var factory = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
