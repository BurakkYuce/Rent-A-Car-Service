using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RentACar.Web.Identity;
using RentACar.Web.Platform;

namespace RentACar.IntegrationTests;

/// <summary>
/// Alan ayrımı middleware'i (PlatformIsolationMiddleware): platform operatörü tenant sayfalarına giremez,
/// konsola yönlendirilir; altyapı yolları (logout/konsol/statik) ve normal tenant kullanıcısı ETKİLENMEZ.
/// Saf mantık → DefaultHttpContext birim testi (web host gerekmez). Beklenenler senaryodan, koddan değil.
/// </summary>
public sealed class PlatformIsolationTests
{
    private static async Task<(int Status, string? Location, bool NextCalled)> RunAsync(
        string path, params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length > 0 ? "test" : null));
        var nextCalled = false;
        await new PlatformIsolationMiddleware().InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });
        return (ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString() is { Length: > 0 } l ? l : null, nextCalled);
    }

    private static Claim Platform => new(PlatformClaims.PlatformAdmin, "true");
    private static Claim Tenant => new(IdentityClaims.TenantId, Guid.NewGuid().ToString());

    [Theory]
    [InlineData("/")]                 // Panel ([Authorize])
    [InlineData("/vehicles")]         // araç listesi
    [InlineData("/kiralar/yeni")]     // kira formu
    [InlineData("/cariler/x/ekstre")] // cari ekstre
    [InlineData("/gibberish-yol")]    // olmayan tenant yolu da konsola (404 yerine)
    [InlineData("/app/panel")]        // F12.2: yeni arayüzün FİRMA ekranları kapalı kalır
    [InlineData("/app/platformx")]    // F12.2: /app/platform muafiyeti segment eşleşmesidir, önek değil
    public async Task Platform_admin_tenant_sayfasindan_konsola_yonlendirilir(string path)
    {
        var (status, location, nextCalled) = await RunAsync(path, Platform);
        Assert.Equal(StatusCodes.Status302Found, status);
        Assert.Equal("/app/platform/kiracilar", location); // F12 kesiş: SPA konsolu (tek adım)
        Assert.False(nextCalled); // istek tenant sayfasına ULAŞMAZ
    }

    [Theory]
    [InlineData("/platform/tenants")]      // konsolun kendisi — döngü olmamalı
    [InlineData("/platform/tenants/abc")]  // detay sayfası
    [InlineData("/auth/logout")]           // ÇIKIŞ çalışmalı (yoksa platform admin kilitlenir)
    [InlineData("/login")]
    [InlineData("/app.css")]               // statik varlık
    [InlineData("/js/rc-ui.js")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/app/platform")]                 // F12.2: SPA platform ekranları (derin bağlantı/yenileme)
    [InlineData("/app/platform/kiracilar")]
    [InlineData("/app/Platform/kiracilar/abc")]   // ASP.NET yönlendirmesi gibi büyük/küçük harf duyarsız
    public async Task Platform_admin_altyapi_yollarina_erisebilir(string path)
    {
        var (_, location, nextCalled) = await RunAsync(path, Platform);
        Assert.True(nextCalled);   // geçer
        Assert.Null(location);     // yönlendirme yok
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/vehicles")]
    public async Task Normal_tenant_kullanicisi_etkilenmez(string path)
    {
        var (_, location, nextCalled) = await RunAsync(path, Tenant); // tenant_id var, platform_admin YOK
        Assert.True(nextCalled);
        Assert.Null(location);
    }

    [Fact]
    public async Task Anonim_istek_etkilenmez_auth_katmani_ele_alir()
    {
        var (_, location, nextCalled) = await RunAsync("/vehicles"); // claim yok → authenticated değil
        Assert.True(nextCalled);
        Assert.Null(location);
    }
}
