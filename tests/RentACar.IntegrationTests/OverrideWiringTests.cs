using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Application.Personnel;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap F3 — Ekran override'ı kritik servislere bağlama. BAĞIMSIZ ORACLE: override YOKKEN floor aynen
/// (servis çalışır); override Admin'i hariç tutunca wired ekran (personel/ayarlar/donem-kapanis) deny-by-default
/// REDDEDER (floor sahibi Admin bile). PermissionGuard floor DEĞİŞMEDİ → mevcut testler yeşil.
/// </summary>
[Collection("postgres")]
public sealed class OverrideWiringTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_override_personel_works()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // Override yok → Admin (floor) çalışır.
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<PersonelService>().ListAsync());
    }

    [Fact]
    public async Task Override_excluding_admin_blocks_personel()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Admin override'ı yalnız Yönetici'ye verir (kendini hariç tutar).
        await sp.GetRequiredService<ScreenPermissionService>().SetAsync("personel", new[] { UserRole.Yonetici });
        // Admin floor'a (ManageUsers) sahip AMA override'da değil → personel ekranı artık RED.
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<PersonelService>().ListAsync());
    }

    [Fact]
    public async Task Override_blocks_ayarlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ScreenPermissionService>().SetAsync("ayarlar", new[] { UserRole.Yonetici });
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<TenantSettingsService>().GetAsync());
    }

    [Fact]
    public async Task Override_blocks_donem_kapanis()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ScreenPermissionService>().SetAsync("donem-kapanis", new[] { UserRole.Operator });
        await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<DonemKilidiService>().LockAsync(Bas));
    }
}
