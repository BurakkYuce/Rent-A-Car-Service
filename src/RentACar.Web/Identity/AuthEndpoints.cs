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
            [Microsoft.AspNetCore.Mvc.FromForm] string sifre,
            // Login.razor'daki gizli alan. Opsiyonel (string? → alan yoksa null, 400 değil); tarayıcıdan
            // geldiği için GÜVENİLMEZ — yalnız YetkiYonlendirme.GuvenliDonus'tan geçmiş hali kullanılır.
            [Microsoft.AspNetCore.Mvc.FromForm(Name = YetkiYonlendirme.DonusParametresi)] string? donus) =>
        {
            var result = await loginService.ValidateAsync(firma, kullanici, sifre);
            if (result is null)
                // Dönüş korunur: şifreyi bir kez yanlış yazan kullanıcı derin bağlantıyı kaybetmesin.
                return Results.Redirect(YetkiYonlendirme.HataliGirisHedefi(donus));

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

            // Varsayılan iniş Panel ("/"); güvenli bir dönüş adresi varsa oraya (bildirim/WhatsApp
            // derin bağlantısı). Eskiden sabit "/vehicles" idi: hem Panel atlanıyor hem derin bağlantı
            // kayboluyordu. LocalRedirect İKİNCİ çittir: GuvenliDonus bir gün gerilese bile yerel
            // olmayan adrese yönlendirmek yerine istisna atar (açık yönlendirme yerine görünür hata).
            return Results.LocalRedirect(YetkiYonlendirme.GuvenliDonus(donus));
        }).AntiforgeryByEnv().RequireRateLimiting("login"); // P0: brute-force koruması

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AntiforgeryByEnv();

        return app;
    }
}
