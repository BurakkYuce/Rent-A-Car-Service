using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// API (JWT) anlık-kesme: platform konsolu bir tenant'ı kapatınca, o tenant'ın ZATEN VERİLMİŞ JWT'si de
/// reddedilmeli (JwtBearerEvents.OnTokenValidated → tenant KAPALI → 401). BAĞIMSIZ ORACLE: elle kurulmuş
/// tenant aktifken authed GET 200; DB'de kapatınca (login anonim → cache boş → ilk authed istek taze okur)
/// aynı token 401. Web'deki TenantActiveMiddleware'in API karşılığı (ortak TenantStatusCache).
/// </summary>
[Collection("postgres")]
public sealed class ApiTenantCutoffTests(PostgresFixture fx)
{
    private static string Uniq() => "cut" + Guid.NewGuid().ToString("N")[..10];

    private async Task SetActiveAsync(string code, bool active)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = await db.Tenants.FirstAsync(x => x.Code == code);
        t.IsActive = active;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Aktif_tenant_authed_istek_gecer()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var code = Uniq();
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "admin", "sifre123", UserRole.Admin);
        var c = await api.LoginClientAsync(code, "admin", "sifre123");

        var resp = await c.GetAsync("/api/v1/vehicles");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // aktif → normal auth çalışıyor (regresyon yok)
    }

    [Fact]
    public async Task Kapali_tenant_zaten_verilmis_token_401()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var code = Uniq();
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "admin", "sifre123", UserRole.Admin);
        var c = await api.LoginClientAsync(code, "admin", "sifre123"); // token AKTİFKEN alındı

        await SetActiveAsync(code, false); // platform konsolu tenant'ı KAPATTI (henüz authed istek yok → cache boş)

        var resp = await c.GetAsync("/api/v1/vehicles"); // ilk authed istek → OnTokenValidated → tenant kapalı → 401
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
