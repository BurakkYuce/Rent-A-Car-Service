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
}
