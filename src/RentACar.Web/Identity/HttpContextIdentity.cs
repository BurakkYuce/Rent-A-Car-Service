using System.Security.Claims;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Web.Identity;

/// <summary>Cookie claim adları (giriş ucu yazar, kimlik buradan okur).</summary>
public static class IdentityClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantCode = "tenant_code";
    public const string UserId = "user_id";
    public const string AssignedBranch = "assigned_sube";
    public const string AssignedBranchId = "assigned_sube_id"; // FAZ 5-C1
    public const string PermissionExtra = "izin_ek";       // kullanıcı-bazlı EK izin (izin adı başına bir claim)
    public const string PermissionDenied = "izin_yasak"; // kullanıcı-bazlı YASAK izin
    public const string SecurityStamp = "security_stamp"; // güvenlik F1 — oturum damgası (SessionStampValidator)
    // Rol, standart ClaimTypes.Role olarak yazılır → [Authorize(Roles="Admin")] doğrudan çalışır.
}

/// <summary>
/// ITenantContext + ICurrentUser'ı HttpContext.User'dan (cookie claim'leri) HER ERİŞİMDE canlı çözer. Web'in tek
/// kimlik kaynağı (F13.1b: Blazor interaktif circuit'i ve onun <c>CircuitTenantContext</c>/<c>HybridIdentity</c>
/// köprüsü kalktı — her istek gerçek bir HTTP isteğidir). Anonim isteklerde null → RLS default-deny.
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
        => User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name : null;

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

    /// <summary>Claim'deki FK; claim boşsa (FK'dan önce açılmış/FK'sız hesap) şube adından çözülen FK
    /// (<see cref="AssignedBranchResolutionMiddleware"/>, kabul B-0).</summary>
    public Guid? AssignedBranchId
        => Guid.TryParse(User.FindFirst(IdentityClaims.AssignedBranchId)?.Value, out var g) ? g
         : accessor.HttpContext?.Items.TryGetValue(AssignedBranchResolutionMiddleware.ItemKey, out var v) == true ? v as Guid? : null;

    public IReadOnlyCollection<string> EkIzinler
        => User.FindAll(IdentityClaims.PermissionExtra).Select(c => c.Value).ToArray();

    public IReadOnlyCollection<string> YasakIzinler
        => User.FindAll(IdentityClaims.PermissionDenied).Select(c => c.Value).ToArray();
}
