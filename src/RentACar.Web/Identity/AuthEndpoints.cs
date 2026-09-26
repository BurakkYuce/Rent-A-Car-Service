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
            [Microsoft.AspNetCore.Mvc.FromForm] string sifre,
            // Login.razor'daki gizli alan. Opsiyonel (string? → alan yoksa null, 400 değil); tarayıcıdan
            // geldiği için GÜVENİLMEZ — yalnız YetkiYonlendirme.GuvenliDonus'tan geçmiş hali kullanılır.
            [Microsoft.AspNetCore.Mvc.FromForm(Name = PermissionRedirect.ReturnParameter)] string? donus) =>
        {
            var result = await loginService.ValidateAsync(firma, kullanici, sifre);
            if (result is null)
                // Dönüş korunur: şifreyi bir kez yanlış yazan kullanıcı derin bağlantıyı kaybetmesin.
                return Results.Redirect(PermissionRedirect.InvalidLoginTarget(donus));

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                SessionPrincipal.Create(result)); // claim seti /api/ui girişiyle ORTAK

            // Varsayılan iniş Panel ("/"); güvenli bir dönüş adresi varsa oraya (bildirim/WhatsApp
            // derin bağlantısı). Eskiden sabit "/vehicles" idi: hem Panel atlanıyor hem derin bağlantı
            // kayboluyordu. LocalRedirect İKİNCİ çittir: GuvenliDonus bir gün gerilese bile yerel
            // olmayan adrese yönlendirmek yerine istisna atar (açık yönlendirme yerine görünür hata).
            return Results.LocalRedirect(PermissionRedirect.SafeReturn(donus));
        }).AntiforgeryByEnv().RequireRateLimiting("login"); // P0: brute-force koruması

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AntiforgeryByEnv();

        return app;
    }
}
