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
                new(IdentityClaims.UserId, result.User.Id.ToString()),
                new(IdentityClaims.TenantId, result.Tenant.Id.ToString()),
                new(IdentityClaims.TenantCode, result.Tenant.Code),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            return Results.Redirect("/vehicles");
        }).AntiforgeryByEnv();

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AntiforgeryByEnv();

        return app;
    }
}
