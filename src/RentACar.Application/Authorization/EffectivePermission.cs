using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// ETKİN izin kararı — TEK doğruluk kaynağı (2026-08-17, kullanıcı-bazlı istisnalar).
///
/// <para>Kural: <c>etkin = !yasak && (matris || ek-izin)</c>. Yasak DAİMA kazanır — rolü ne olursa
/// olsun (Admin dahil: bir admin başka admini kısabilir; kendini kısamaz, servis engeller).</para>
///
/// <para>Hem servis guard'ı (<see cref="PermissionGuard"/>) hem web policy'leri buradan geçer;
/// iki katman aynı kuralı iki bağımsız girdiden (ICurrentUser / ClaimsPrincipal) uygular.
/// İstisna adları STRING gelir (Domain, Permission enum'unu bilmez); bilinmeyen ad sessizce yok
/// sayılır — eski bir claim yeni koda zarar veremez.</para>
/// </summary>
public static class EffectivePermission
{
    public static bool Has(ICurrentUser user, Permission permission)
        => Has(user.Role, permission, user.EkIzinler, user.YasakIzinler);

    /// <summary>Claim-düzeyi karar (web policy'leri ham claim listeleriyle çağırır).</summary>
    public static bool Has(
        UserRole? role, Permission permission,
        IReadOnlyCollection<string> ekIzinler, IReadOnlyCollection<string> yasakIzinler)
    {
        var ad = permission.ToString();
        if (yasakIzinler.Contains(ad)) return false;                    // yasak her şeyi keser
        return RolePermissions.Has(role, permission) || ekIzinler.Contains(ad);
    }
}
