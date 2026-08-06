using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Users;

namespace RentACar.Web.Identity;

/// <summary>
/// FAZ-83 — Kullanıcının kendi profil işlemleri. <see cref="RentACar.Web.Users.UserEndpoints"/>'ten
/// AYRI bir grup olması bilinçli: orası <c>RequireRole(Admin)</c> ile kilitli, burası HERHANGİ giriş
/// yapmış kullanıcıya açık.
///
/// <para><b>Güvenlik sözleşmesi:</b> bu gruptaki hiçbir uç kullanıcı kimliğini FORM'DAN ALMAZ.
/// Kimlik daima <c>ICurrentUser.UserId</c>'den okunur (servis içinde). Aşağıdaki uçta bir <c>id</c>
/// parametresi görürseniz bu bir yetki-yükseltme açığıdır: giriş yapmış herhangi biri başkasının
/// id'sini post ederek onun parolasını değiştirebilir. Parametre listesi bu yüzden kasıtlı olarak
/// yalnız <c>eskiSifre</c> + <c>yeniSifre</c> içerir.</para>
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        // Rol kısıtı YOK — yalnız "giriş yapmış olmak" yeter.
        var grp = app.MapGroup("/profil").RequireAuthorization().AntiforgeryByEnv();

        // POST alt-yola: sayfanın kendisi `@page "/profil/sifre-degistir"`. Aynı yola hem @page hem
        // MapPost koymak AmbiguousMatchException (500) üretir ve hiçbir test yakalamaz — proje
        // konvansiyonu POST'u daima alt-yola koymak (/ayarlar → /ayarlar/kaydet deseni).
        grp.MapPost("/sifre-degistir/kaydet", async (UserService svc,
            [FromForm] string eskiSifre, [FromForm] string yeniSifre, [FromForm] string? yeniSifreTekrar) =>
        {
            try
            {
                // Tekrar alanı yazım-hatası koruması (güvenlik sınırı değil) ve SUNUCUDA kontrol
                // edilir: CSP `script-src 'self'` olduğu için inline JS ile yapılamaz, ayrıca
                // form JS'siz de çalışmalı. Alan hiç gönderilmezse kontrol atlanır — zararsız.
                if (yeniSifreTekrar is not null && !string.Equals(yeniSifre, yeniSifreTekrar, StringComparison.Ordinal))
                    throw new ValidationException("Yeni parolalar birbiriyle eşleşmiyor.");
                await svc.ChangeOwnPasswordAsync(eskiSifre, yeniSifre);
                return Results.Redirect("/profil/sifre-degistir?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/profil/sifre-degistir?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
