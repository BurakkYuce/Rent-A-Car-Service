using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.Authorization;

/// <summary>
/// Servis-katmanı yetki guard'ı. İzin yoksa ValidationException (web bunu kullanıcıya hata
/// olarak gösterir; web ayrıca RequireRole ile erişimi keser → çift savunma).
/// </summary>
public static class PermissionGuard
{
    public static void Require(ICurrentUser user, Permission permission)
    {
        if (!RolePermissions.Has(user.Role, permission))
            throw new ValidationException($"Bu işlem için yetkiniz yok ({permission}).");
    }
}
