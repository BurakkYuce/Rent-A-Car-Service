using RentACar.Application.Authorization;

namespace RentACar.Api.Common;

/// <summary>
/// Endpoint yetkilendirme: bir izne sahip rolleri RequireRole'e çevirir → yetkisiz istek 403
/// alır (servis katmanı PermissionGuard ile ikinci savunma; o 400 yerine endpoint 403 verir).
/// </summary>
public static class ApiAuthExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, Permission permission)
        => builder.RequireAuthorization(policy => policy.RequireRole(RolePermissions.RolesWith(permission)));

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, Permission permission)
    {
        builder.RequireAuthorization(policy => policy.RequireRole(RolePermissions.RolesWith(permission)));
        return builder;
    }
}
