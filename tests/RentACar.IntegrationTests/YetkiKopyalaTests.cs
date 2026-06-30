using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap M2 — rol yetki kopyala (yetki şablonu). BAĞIMSIZ ORACLE: kaynak rolün bulunduğu ekran override'ına
/// hedef rol EKLENİR (yalnız ekleme); idempotent (ikinci kopyada 0); aynı rol red; tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class YetkiKopyalaTests(PostgresFixture fx)
{
    [Fact]
    public async Task Kopyala_hedef_rolu_ekler_ve_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ScreenPermissionService>();

        // İki ekran: biri Yonetici'li (kopyalanmalı), biri yalnız Admin (dokunulmamalı).
        await svc.SetAsync("rapor-x", [UserRole.Admin, UserRole.Yonetici]);
        await svc.SetAsync("admin-only", [UserRole.Admin]);

        var guncellenen = await svc.KopyalaRolAsync(UserRole.Yonetici, UserRole.Operator);
        Assert.Equal(1, guncellenen); // yalnız rapor-x

        var list = await svc.ListAsync();
        var raporX = list.Single(s => s.EkranKodu == "rapor-x").AllowedRolesCsv;
        Assert.Contains("Operator", raporX);
        Assert.Contains("Yonetici", raporX);     // mevcut korundu
        Assert.Contains("Admin", raporX);
        var adminOnly = list.Single(s => s.EkranKodu == "admin-only").AllowedRolesCsv;
        Assert.DoesNotContain("Operator", adminOnly); // Yonetici yoktu → dokunulmadı

        // İdempotent: Operator zaten eklendi → ikinci kopya 0 değiştirir
        Assert.Equal(0, await svc.KopyalaRolAsync(UserRole.Yonetici, UserRole.Operator));
    }

    [Fact]
    public async Task Ayni_rol_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ScreenPermissionService>();
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => svc.KopyalaRolAsync(UserRole.Admin, UserRole.Admin));
    }

    [Fact]
    public async Task Tenant_izolasyon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<ScreenPermissionService>()
                .SetAsync("rapor-x", [UserRole.Yonetici]);

        // t2 kopyalama yapsa bile t1'in ekranı etkilenmez (kendi boş seti).
        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Equal(0, await s2.ServiceProvider.GetRequiredService<ScreenPermissionService>()
            .KopyalaRolAsync(UserRole.Yonetici, UserRole.Operator));

        using var s1b = host.ScopeFor(t1);
        var raporX = (await s1b.ServiceProvider.GetRequiredService<ScreenPermissionService>().ListAsync())
            .Single(s => s.EkranKodu == "rapor-x").AllowedRolesCsv;
        Assert.DoesNotContain("Operator", raporX); // t2'nin kopyası t1'e sızmadı
    }
}
