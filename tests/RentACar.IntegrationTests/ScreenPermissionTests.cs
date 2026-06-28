using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap E3 — ScreenPermissionService (override katmanı). BAĞIMSIZ ORACLE: override yok→floor; override
/// var→deny-by-default tightening; override floor'u aşamaz; tenant izolasyon; yönetim ManageUsers.
/// PermissionGuard (floor) DEĞİŞMEZ — mevcut auth testleri etkilenmez.
/// </summary>
[Collection("postgres")]
public sealed class ScreenPermissionTests(PostgresFixture fx)
{
    private static ScreenPermissionService Svc(IServiceScope s) => s.ServiceProvider.GetRequiredService<ScreenPermissionService>();

    [Fact]
    public async Task No_override_falls_to_floor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        using (var op = host.ScopeFor(t, role: UserRole.Operator))
            Assert.True(await Svc(op).IsScreenAllowedAsync("ekranA", Permission.OperationsWrite)); // floor var, override yok

        using var muh = host.ScopeFor(t, role: UserRole.Muhasebe);
        Assert.False(await Svc(muh).IsScreenAllowedAsync("ekranA", Permission.OperationsWrite)); // floor yok
    }

    [Fact]
    public async Task Override_tightens_deny_by_default()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
            await Svc(admin).SetAsync("ekranB", new[] { UserRole.Admin }); // yalnız Admin

        // Operatör OperationsWrite floor'a sahip AMA override'da değil → artık RED.
        using (var op = host.ScopeFor(t, role: UserRole.Operator))
            Assert.False(await Svc(op).IsScreenAllowedAsync("ekranB", Permission.OperationsWrite));

        // Admin override'da + floor → izin.
        using var ad = host.ScopeFor(t, role: UserRole.Admin);
        Assert.True(await Svc(ad).IsScreenAllowedAsync("ekranB", Permission.OperationsWrite));
    }

    [Fact]
    public async Task Override_cannot_exceed_floor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
            await Svc(admin).SetAsync("ekranC", new[] { UserRole.Muhasebe }); // Muhasebe'nin OperationsWrite floor'u yok

        using var muh = host.ScopeFor(t, role: UserRole.Muhasebe);
        Assert.False(await Svc(muh).IsScreenAllowedAsync("ekranC", Permission.OperationsWrite)); // floor yok → RED
    }

    [Fact]
    public async Task Override_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var admin1 = host.ScopeFor(t1, role: UserRole.Admin))
            await Svc(admin1).SetAsync("ekranD", new[] { UserRole.Admin });

        // t2'de override yok → Operatör floor'a göre erişir (t1 override'ı sızmaz).
        using var op2 = host.ScopeFor(t2, role: UserRole.Operator);
        Assert.True(await Svc(op2).IsScreenAllowedAsync("ekranD", Permission.OperationsWrite));
    }

    [Fact]
    public async Task Management_requires_manageusers()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var op = host.ScopeFor(Guid.NewGuid(), role: UserRole.Operator);
        await Assert.ThrowsAsync<ValidationException>(() => Svc(op).SetAsync("x", new[] { UserRole.Admin }));
        await Assert.ThrowsAsync<ValidationException>(() => Svc(op).ListAsync());
    }

    [Fact]
    public async Task EnsureScreenAccess_throws_when_denied()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
            await Svc(admin).SetAsync("ekranE", new[] { UserRole.Admin });

        using var op = host.ScopeFor(t, role: UserRole.Operator);
        await Assert.ThrowsAsync<ValidationException>(() => Svc(op).EnsureScreenAccessAsync("ekranE", Permission.OperationsWrite));
    }
}
