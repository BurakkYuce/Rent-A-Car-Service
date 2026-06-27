using RentACar.Application.Authorization;

namespace RentACar.Web.Identity;

/// <summary>
/// Endpoint gruplarını yetki matrisine (RolePermissions) göre rol-gate eder. Matris tek
/// doğruluk kaynağı → web ve servis guard'ları aynı kuralı paylaşır.
/// </summary>
public static class AuthExtensions
{
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder group, Permission permission)
        => group.RequireAuthorization(p => p.RequireRole(RolePermissions.RolesWith(permission)));
}
