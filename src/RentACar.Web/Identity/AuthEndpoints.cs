using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RentACar.Web.Identity;

/// <summary>
/// Eski form çıkış ucu (<c>POST /auth/logout</c>). F13.1b: Blazor giriş formu ve <c>POST /auth/login</c> kalktı — giriş
/// yalnız yeni arayüzün oturum ucunda (<c>/api/ui/v1/oturum/giris</c>). Çıkış ucu açık kalmış eski sekmeler için yaşar;
/// yeni arayüzün çıkışıyla (<c>POST /api/ui/v1/oturum/cikis</c>) AYNI işi yapar (tek çerez şemasından çıkış) ve SPA
/// girişine <c>?neden=cikis</c> ile döner.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect(RentACar.Web.Spa.Cutover.SpaLogin + "?"
                + RentACar.Web.Spa.Cutover.SpaReasonParameter + "=cikis");
        }).AntiforgeryByEnv().AllowAnonymous(); // açık karar: süresi dolmuş oturumla da çıkış çalışmalı

        return app;
    }
}
