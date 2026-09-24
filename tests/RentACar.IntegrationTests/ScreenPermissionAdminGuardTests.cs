using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #304 L3 — Admin'in ekran erişimine dokunan override yazımı yalnız Admin rolüne açıktır. ManageUsers istisnası almış
/// Yönetici Admin'i bir listeden çıkaramaz, Admin'i ekleyemez, Admin'i dışlayan override'ı kaldıramaz; Admin'e dokunmayan
/// değişiklikleri yapabilir. Beklenen değerler elle kurulmuş senaryodan (bağımsız oracle).
/// </summary>
[Collection("postgres")]
public sealed class ScreenPermissionAdminGuardTests(PostgresFixture fx)
{
    private static IServiceScope Manager(TestHost host, Guid tenant)
    {
        var scope = host.ScopeFor(tenant, role: UserRole.Yonetici);
        scope.ServiceProvider.GetRequiredService<TestIdentity>().EkIzinler = ["ManageUsers"];
        return scope;
    }

    private static async Task<string?> CsvAsync(TestHost host, Guid tenant, string code)
    {
        using var admin = host.ScopeFor(tenant, role: UserRole.Admin);
        return (await admin.ServiceProvider.GetRequiredService<ScreenPermissionService>().ListAsync())
            .SingleOrDefault(s => s.EkranKodu == code)?.AllowedRolesCsv;
    }

    [Fact]
    public async Task Manager_with_exception_cannot_remove_or_add_admin_in_set()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
        {
            var a = admin.ServiceProvider.GetRequiredService<ScreenPermissionService>();
            await a.SetAsync("rapor-a", [UserRole.Admin, UserRole.Yonetici]);
            await a.SetAsync("rapor-b", [UserRole.Yonetici]);
        }
        using var m = Manager(host, t);
        var svc = m.ServiceProvider.GetRequiredService<ScreenPermissionService>();

        // Admin'i listeden çıkarmak ve yeni ekranı Admin'siz kurmak reddedilir.
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.SetAsync("rapor-a", [UserRole.Yonetici]));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.SetAsync("yeni-ekran", [UserRole.Operator]));
        // Admin'i dışlayan override'a Admin eklemek, onu pasifleştirmek ya da silmek de Admin'e dokunmaktır.
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.SetAsync("rapor-b", [UserRole.Yonetici, UserRole.Admin]));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.SetAsync("rapor-b", [UserRole.Yonetici], aktif: false));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.RemoveAsync("rapor-b"));
        Assert.Equal("Admin,Yonetici", await CsvAsync(host, t, "rapor-a"));
        Assert.Equal("Yonetici", await CsvAsync(host, t, "rapor-b"));
        Assert.Null(await CsvAsync(host, t, "yeni-ekran"));

        // Admin'e dokunmayan değişiklik serbest.
        await svc.SetAsync("rapor-a", [UserRole.Admin, UserRole.Yonetici, UserRole.Muhasebe]);
        await svc.SetAsync("rapor-b", [UserRole.Yonetici, UserRole.Operator]);
        Assert.Equal("Admin,Yonetici,Muhasebe", await CsvAsync(host, t, "rapor-a"));
        Assert.Equal("Yonetici,Operator", await CsvAsync(host, t, "rapor-b"));
    }

    [Fact]
    public async Task Manager_with_exception_cannot_copy_role_into_admin()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
            await admin.ServiceProvider.GetRequiredService<ScreenPermissionService>().SetAsync("rapor-b", [UserRole.Yonetici]);
        using var m = Manager(host, t);
        var svc = m.ServiceProvider.GetRequiredService<ScreenPermissionService>();

        await Assert.ThrowsAsync<YetkiYokException>(() => svc.KopyalaRolAsync(UserRole.Yonetici, UserRole.Admin));
        Assert.Equal("Yonetici", await CsvAsync(host, t, "rapor-b"));
        // Admin'e dokunmayan kopya serbest.
        Assert.Equal(1, await svc.KopyalaRolAsync(UserRole.Yonetici, UserRole.Operator));

        // Admin rolü aynı kopyayı yapabilir.
        using var admin2 = host.ScopeFor(t, role: UserRole.Admin);
        Assert.Equal(1, await admin2.ServiceProvider.GetRequiredService<ScreenPermissionService>()
            .KopyalaRolAsync(UserRole.Yonetici, UserRole.Admin));
        Assert.Equal("Yonetici,Operator,Admin", await CsvAsync(host, t, "rapor-b"));
    }

    [Fact]
    public async Task Manager_with_exception_cannot_apply_template_that_touches_admin_and_nothing_is_written()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
        {
            var a = admin.ServiceProvider.GetRequiredService<ScreenPermissionService>();
            // Şablon: rapor-a Admin'siz, rapor-c Admin'li.
            await a.SetAsync("rapor-c", [UserRole.Admin, UserRole.Muhasebe]);
            await a.SetAsync("rapor-a", [UserRole.Yonetici]);
            await a.SnapshotGrupAsync("kisitli");
            // Güncel durum: ikisi de Admin'li.
            await a.SetAsync("rapor-a", [UserRole.Admin, UserRole.Yonetici]);
            await a.SetAsync("rapor-c", [UserRole.Admin]);
        }
        using var m = Manager(host, t);
        var svc = m.ServiceProvider.GetRequiredService<ScreenPermissionService>();

        await Assert.ThrowsAsync<YetkiYokException>(() => svc.UygulaGrupAsync("kisitli"));
        // Yarım uygulama yok: Admin'e dokunmayan rapor-c kalemi de yazılmadı.
        Assert.Equal("Admin,Yonetici", await CsvAsync(host, t, "rapor-a"));
        Assert.Equal("Admin", await CsvAsync(host, t, "rapor-c"));

        using var admin2 = host.ScopeFor(t, role: UserRole.Admin);
        Assert.Equal(2, await admin2.ServiceProvider.GetRequiredService<ScreenPermissionService>().UygulaGrupAsync("kisitli"));
        Assert.Equal("Yonetici", await CsvAsync(host, t, "rapor-a"));
        Assert.Equal("Admin,Muhasebe", await CsvAsync(host, t, "rapor-c"));
    }
}
