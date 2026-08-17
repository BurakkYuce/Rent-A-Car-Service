using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RentACar.Infrastructure.Identity;

namespace RentACar.Web.Identity;

/// <summary>
/// Login/logout için minimal API uçları. Cookie SignIn yalnız gerçek HTTP isteğinde
/// (HttpContext) yapılabildiğinden, Blazor SSR login formu buraya POST eder.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (
            HttpContext http,
            LoginService loginService,
            [Microsoft.AspNetCore.Mvc.FromForm] string firma,
            [Microsoft.AspNetCore.Mvc.FromForm] string kullanici,
            [Microsoft.AspNetCore.Mvc.FromForm] string sifre) =>
        {
            var result = await loginService.ValidateAsync(firma, kullanici, sifre);
            if (result is null)
                return Results.Redirect("/login?hata=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.User.UserName),
                new(ClaimTypes.Role, result.User.Rol.ToString()),
                new(IdentityClaims.AssignedBranch, result.User.AtanmisSube ?? ""),
                new(IdentityClaims.AssignedBranchId, result.User.AtanmisSubeId?.ToString() ?? ""), // FAZ 5-C1
                new(IdentityClaims.UserId, result.User.Id.ToString()),
                new(IdentityClaims.TenantId, result.Tenant.Id.ToString()),
                new(IdentityClaims.TenantCode, result.Tenant.Code),
            };
            // Kullanıcı-bazlı istisnalar: izin adı başına BİR claim (CSV değil — FindAll ile
            // ayrıştırmasız okunur). Değişiklik sonraki girişte etkinleşir (claim login'de donar).
            claims.AddRange(result.EkIzinler.Select(i => new Claim(IdentityClaims.IzinEk, i)));
            claims.AddRange(result.YasakIzinler.Select(i => new Claim(IdentityClaims.IzinYasak, i)));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            return Results.Redirect("/vehicles");
        }).AntiforgeryByEnv().RequireRateLimiting("login"); // P0: brute-force koruması

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AntiforgeryByEnv();

        return app;
    }
}
