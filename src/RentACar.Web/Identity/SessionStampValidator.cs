using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RentACar.Infrastructure.Identity;

namespace RentACar.Web.Identity;

/// <summary>
/// Güvenlik F1 — çerez oturumunun her istekte doğrulanması. Rol/şube/aktiflik/parola/izin istisnası değişince kullanıcının
/// oturum damgası (Users.GuvenlikDamgasi) yenilenir; çerezdeki damga DB'dekiyle (kısa önbellekle) eşleşmezse, kullanıcı
/// pasifse ya da satır yoksa oturum REDDEDİLİR ve çerez silinir. Önceden çerez kayan süreli olduğundan rolü düşürülen
/// Admin'in açık oturumu süresiz Admin kalıyor, yeni Admin açıp kalan Admin'in parolasını sıfırlayabiliyordu.
/// <list type="bullet">
/// <item>Claim'ler DEĞİŞTİRİLMEZ (yalnız ret) — antiforgery belirteci claim kümesine bağlı; claim oynatmak geçerli
/// oturumların XSRF'ini bozar (B-0'da ödenen ders).</item>
/// <item>Damga claim'i olmayan (bu sürümden önce açılmış) oturum bir kez yeniden girişe düşer — damgasız oturumun
/// güncelliği kanıtlanamaz.</item>
/// <item>Platform operatörü (tenant/kullanıcı kimliği yok) bu kontrole girmez; o oturum kendi kapısıyla korunur.</item>
/// </list>
/// </summary>
public static class SessionStampValidator
{
    /// <summary>Saf karar: oturum geçerli mi? (state null = satır yok)</summary>
    public static bool IsValid(string? claimStamp, UserSessionState? state)
        => claimStamp is not null && state is { Active: true } && string.Equals(claimStamp, state.Stamp, StringComparison.Ordinal);

    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        if (principal?.Identity?.IsAuthenticated != true) return;
        if (!Guid.TryParse(principal.FindFirst(IdentityClaims.UserId)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(IdentityClaims.TenantId)?.Value, out var tenantId))
            return; // platform operatörü vb.

        var cache = ctx.HttpContext.RequestServices.GetRequiredService<UserSessionStateCache>();
        var state = await cache.GetAsync(tenantId, userId, ctx.HttpContext.RequestAborted);
        if (IsValid(principal.FindFirst(IdentityClaims.SecurityStamp)?.Value, state)) return;

        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
