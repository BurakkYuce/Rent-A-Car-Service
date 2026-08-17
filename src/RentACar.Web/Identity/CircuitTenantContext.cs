using System.Security.Claims;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Web.Identity;

/// <summary>
/// İnteraktif Server circuit için tenant/kullanıcı bağlamı. HttpContext circuit içinde null olduğundan
/// (yalnız SignalR handshake'te var), <see cref="HttpContextIdentity"/> circuit'te işlevsizdir → tenant
/// null → RLS default-deny → 0 satır. Bu bağlam, kimliği <b>circuit init'te bir kez</b> authenticated
/// <see cref="ClaimsPrincipal"/>'dan (<see cref="SetFrom"/>) yakalayıp circuit ömrü boyunca tutar.
/// Alt katman (TenantConnectionInterceptor → app.tenant_id GUC → Postgres RLS; ScopedAppDbContextFactory)
/// yalnız <see cref="ITenantContext"/>'e bağlı olduğundan DEĞİŞMEZ — tek boşluk kimlik kaynağıdır.
/// Claim adları <see cref="HttpContextIdentity"/> ile birebir aynı (<see cref="IdentityClaims"/>).
/// </summary>
public sealed class CircuitTenantContext : ITenantContext, ICurrentUser
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public UserRole? Role { get; private set; }
    public string? AssignedBranch { get; private set; }
    public Guid? AssignedBranchId { get; private set; } // FAZ 5-C1
    public IReadOnlyCollection<string> EkIzinler { get; private set; } = [];    // kullanıcı-bazlı istisna
    public IReadOnlyCollection<string> YasakIzinler { get; private set; } = []; // (login claim'inden)

    /// <summary>Kimlik circuit'te dolduruldu mu (RLS-in-circuit teşhisi için).</summary>
    public bool IsSet { get; private set; }

    /// <summary>Circuit init'te authenticated principal'dan doldur (HttpContextIdentity ile aynı claim'ler).</summary>
    public void SetFrom(ClaimsPrincipal user)
    {
        TenantId = Guid.TryParse(user.FindFirst(IdentityClaims.TenantId)?.Value, out var t) ? t : null;
        UserId = Guid.TryParse(user.FindFirst(IdentityClaims.UserId)?.Value, out var u) ? u : null;
        UserName = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        Role = Enum.TryParse<UserRole>(user.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;
        var branch = user.FindFirst(IdentityClaims.AssignedBranch)?.Value;
        AssignedBranch = string.IsNullOrWhiteSpace(branch) ? null : branch;
        AssignedBranchId = Guid.TryParse(user.FindFirst(IdentityClaims.AssignedBranchId)?.Value, out var bid)
            ? bid : null; // FAZ 5-C1 (eski oturum claim'siz → null, hatasız)
        EkIzinler = user.FindAll(IdentityClaims.IzinEk).Select(c => c.Value).ToArray();
        YasakIzinler = user.FindAll(IdentityClaims.IzinYasak).Select(c => c.Value).ToArray();
        IsSet = true;
    }
}
