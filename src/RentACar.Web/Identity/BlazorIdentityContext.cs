using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Web.Identity;

/// <summary>Cookie claim adları (login endpoint'i yazar, identity buradan okur).</summary>
public static class IdentityClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantCode = "tenant_code";
    public const string UserId = "user_id";
    public const string AssignedBranch = "assigned_sube";
    // Rol, standart ClaimTypes.Role olarak yazılır → [Authorize(Roles="Admin")] doğrudan çalışır.
}

/// <summary>
/// ITenantContext + ICurrentUser'ı HttpContext.User'dan (cookie claim'leri) çözer.
/// Araç ekranları static SSR olduğundan her etkileşim gerçek bir HTTP isteğidir →
/// HttpContext (ve tenant claim'i) daima mevcuttur. Anonim isteklerde null → RLS
/// default-deny.
/// </summary>
public sealed class HttpContextIdentity(IHttpContextAccessor accessor) : ITenantContext, ICurrentUser
{
    private ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public Guid? TenantId
        => Guid.TryParse(User.FindFirst(IdentityClaims.TenantId)?.Value, out var g) ? g : null;

    public Guid? UserId
        => Guid.TryParse(User.FindFirst(IdentityClaims.UserId)?.Value, out var g) ? g : null;

    public string? UserName
        => User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;

    public UserRole? Role
        => Enum.TryParse<UserRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;

    public string? AssignedBranch
    {
        get
        {
            var v = User.FindFirst(IdentityClaims.AssignedBranch)?.Value;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
    }
}

/// <summary>
/// SSR için kimlik durumu sağlayıcısı: AuthorizeView / [Authorize] HttpContext.User'a
/// dayanır. (İnteraktif circuit YOK; static SSR'da HttpContext daima var.)
/// </summary>
public sealed class SsrAuthenticationStateProvider(IHttpContextAccessor accessor) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(new AuthenticationState(
            accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity())));
}
