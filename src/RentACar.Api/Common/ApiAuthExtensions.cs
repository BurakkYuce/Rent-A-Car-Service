using System.Security.Claims;
using RentACar.Application.Authorization;
using RentACar.Api.Identity;
using RentACar.Domain.Enums;

namespace RentACar.Api.Common;

/// <summary>
/// Endpoint yetkilendirme — ETKİN izin (rol matrisi + kullanıcı-bazlı istisna claim'leri).
/// 2026-08-17'ye kadar RequireRole'dü; istisnalarla iki sorun doğdu: ek izinli kullanıcı API'den
/// 403 yiyordu (web'de girebildiği yere) ve yasaklı kullanıcı API kapısından geçip ancak servis
/// guard'ında düşüyordu (çift savunmanın dış katmanı deliniyordu). Karar fonksiyonu web'dekiyle
/// AYNI (EffectivePermission) — iki host ayrışamaz.
/// </summary>
public static class ApiAuthExtensions
{
    private static bool HasPermission(ClaimsPrincipal user, Permission permission)
    {
        var role = Enum.TryParse<UserRole>(user.FindFirst(ClaimTypes.Role)?.Value, out var r)
            ? r : (UserRole?)null;
        return EffectivePermission.Has(role, permission,
            user.FindAll(ApiClaims.IzinEk).Select(c => c.Value).ToArray(),
            user.FindAll(ApiClaims.IzinYasak).Select(c => c.Value).ToArray());
    }

    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, Permission permission)
        => builder.RequireAuthorization(policy => policy.RequireAssertion(ctx => HasPermission(ctx.User, permission)));

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, Permission permission)
    {
        builder.RequireAuthorization(policy => policy.RequireAssertion(ctx => HasPermission(ctx.User, permission)));
        return builder;
    }
}
