using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.Authorization;

/// <summary>
/// Servis-katmanı yetki guard'ı. İzin yoksa YetkiYokException (ValidationException alt tipi; web bunu kullanıcıya hata
/// olarak gösterir; web ayrıca RequireRole ile erişimi keser → çift savunma).
/// </summary>
public static class PermissionGuard
{
    public static void Require(ICurrentUser user, Permission permission)
    {
        // 2026-08-17: karar artık EffectivePermission'dan — rol matrisi + kullanıcı-bazlı
        // ek izin/yasak bileşimi. İstisnası olmayan kullanıcıda davranış birebir eski matris.
        if (!EffectivePermission.Has(user, permission))
            throw new YetkiYokException($"Bu işlem için yetkiniz yok ({permission}).");
    }

    /// <summary>
    /// İzinlerden HERHANGİ BİRİ yeter (FAZ-45). Gerekçe: operasyonel bir kaydı YAZABİLEN rol onu
    /// OKUYAMIYORSA ekran kullanılamaz hâle gelir — vardiya raporunda Operatör tam olarak bu
    /// durumdaydı (OperationsWrite var, ViewReports yok). Genel rapor okuması hâlâ ViewReports'a
    /// bağlı; bu overload yalnız "kendi yazdığını görme" vakası için.
    /// </summary>
    public static void RequireAny(ICurrentUser user, params Permission[] permissions)
    {
        if (!permissions.Any(p => EffectivePermission.Has(user, p)))
            throw new YetkiYokException(
                $"Bu işlem için yetkiniz yok ({string.Join(" veya ", permissions)}).");
    }
}
