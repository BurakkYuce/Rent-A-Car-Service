using System.Security.Claims;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Api.Identity;

/// <summary>JWT claim adları. Web cookie claim'leriyle AYNI değerler → servisler/RLS ortak.</summary>
public static class ApiClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantCode = "tenant_code";
    public const string UserId = "user_id";
    public const string AssignedBranch = "assigned_sube";
    public const string AssignedBranchId = "assigned_sube_id"; // FAZ 5-C1
    public const string PermissionExtra = "izin_ek";       // kullanıcı-bazlı EK izin (web ile aynı ad)
    public const string PermissionDenied = "izin_yasak"; // kullanıcı-bazlı YASAK izin
    // Rol standart ClaimTypes.Role; ad standart ClaimTypes.Name.
}

/// <summary>
/// ITenantContext + ICurrentUser'ı HttpContext.User'dan (JWT bearer claim'leri) çözer.
/// JWT doğrulanınca claim'ler HttpContext.User'a düşer → TenantConnectionInterceptor
/// app.tenant_id GUC'unu buradan set eder → Postgres RLS uygulanır. Anonim/token yok →
/// TenantId null → RLS default-deny.
/// </summary>
public sealed class ApiIdentity(IHttpContextAccessor accessor) : ITenantContext, ICurrentUser
{
    private ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public Guid? TenantId
        => Guid.TryParse(User.FindFirst(ApiClaims.TenantId)?.Value, out var g) ? g : null;

    public Guid? UserId
        => Guid.TryParse(User.FindFirst(ApiClaims.UserId)?.Value, out var g) ? g : null;

    public string? UserName
        => User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;

    public UserRole? Role
        => Enum.TryParse<UserRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;

    public string? AssignedBranch
    {
        get
        {
            var v = User.FindFirst(ApiClaims.AssignedBranch)?.Value;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
    }

    public Guid? AssignedBranchId
        => Guid.TryParse(User.FindFirst(ApiClaims.AssignedBranchId)?.Value, out var g) ? g : null; // FAZ 5-C1

    // İstisnalar JWT'den okunur — okunmasaydı API, web'de kısılmış kullanıcıya yan kapı olurdu
    // (servis guard'ı ICurrentUser'dan besleniyor; boş dönen varsayılan yasakları görmezdi).
    public IReadOnlyCollection<string> EkIzinler
        => User.FindAll(ApiClaims.PermissionExtra).Select(c => c.Value).ToArray();

    public IReadOnlyCollection<string> YasakIzinler
        => User.FindAll(ApiClaims.PermissionDenied).Select(c => c.Value).ToArray();
}
