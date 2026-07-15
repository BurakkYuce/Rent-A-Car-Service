using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>Tenant'sız context (design-time / migration üretimi için).</summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();
    public Guid? TenantId => null;
}

/// <summary>Kullanıcısız context (design-time için).</summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public static readonly NullCurrentUser Instance = new();
    public Guid? UserId => null;
    public string? UserName => null;
    public UserRole? Role => null;
    public string? AssignedBranch => null;
    public Guid? AssignedBranchId => null; // FAZ 5-C1
}

/// <summary>
/// Arka-plan iş (scheduler) için değiştirilebilir tenant bağlamı: HttpContext yoktur, iş tenant'ı
/// döngüde tek tek set eder. TenantConnectionInterceptor bunu okuyup app.tenant_id GUC'unu ayarlar
/// (RLS), query filter + interceptor damgası da bunu kullanır. Kullanıcı yok (sistem işi).
/// </summary>
public sealed class SystemTenantContext : ITenantContext, ICurrentUser
{
    public Guid? TenantId { get; set; }
    public Guid? UserId => null;
    public string? UserName => "sistem";
    public UserRole? Role => null;
    public string? AssignedBranch => null;
    public Guid? AssignedBranchId => null; // FAZ 5-C1
}
