using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.PublicSite;

/// <summary>
/// İstek-scoped tenant bağlamı (PR-1). <see cref="RentACar.Infrastructure.Persistence.SystemTenantContext"/>
/// ile yapısal olarak aynı, ama arka-plan job değil, HER İSTEK için ayrı bir `AddScoped` örneği: PR-2'nin
/// host-çözümleme middleware'i istek başına <see cref="TenantId"/>'yi doldurur, normal DI/`IDbContextFactory`
/// akışındaki `TenantConnectionInterceptor` gerisini (RLS GUC) otomatik halleder.
/// </summary>
public sealed class PublicTenantContext : ITenantContext, ICurrentUser
{
    public Guid? TenantId { get; set; }
    public Guid? UserId => null;
    public string? UserName => "public-site";
    public UserRole? Role => null;
    public string? AssignedBranch => null;
    public Guid? AssignedBranchId => null;
}
