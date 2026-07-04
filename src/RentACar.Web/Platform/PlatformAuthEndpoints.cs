using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using RentACar.Web.Identity;

namespace RentACar.Web.Platform;

/// <summary>
/// Platform operatörü login/logout. Config kimliğini doğrular → varsayılan cookie şemasına SignIn;
/// claim'ler SADECE ad + <c>platform_admin=true</c> (tenant_id/rol YOK → ITenantContext null → RLS deny).
/// Tek cookie şeması: platform girişi olası tenant oturumunu geçersiz kılar (tek operatör için kabul).
/// </summary>
public static class PlatformAuthEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/platform/auth/login", async (
            HttpContext http, PlatformCredentials creds,
            [FromForm] string kullanici, [FromForm] string sifre) =>
        {
            if (!creds.Verify(kullanici ?? "", sifre ?? ""))
                return Results.Redirect("/platform/login?hata=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, creds.User),
                new(PlatformClaims.PlatformAdmin, "true"),
                // tenant_id / rol claim'i BİLİNÇLİ YOK → tenant verisine erişemez.
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/platform/tenants");
        }).AntiforgeryByEnv().RequireRateLimiting("login"); // brute-force koruması (operatör parolası)

        app.MapPost("/platform/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/platform/login");
        }).AntiforgeryByEnv();

        return app;
    }
}
