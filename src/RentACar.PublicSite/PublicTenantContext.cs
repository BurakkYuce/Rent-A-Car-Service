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

    /// <summary>PR-4.5: TenantHostResolutionMiddleware ya `Found` olup TenantId'yi doldurur ya da
    /// `next()`'i hiç çağırmadan 404 döner — bu yüzden aşağı akışta TenantId'nin boş olması YALNIZ
    /// bir bug ihtimali, meşru bir senaryo değil. Sessiz default-deny yerine gürültülü hata istenir.</summary>
    public bool ThrowIfTenantMissing => true;
}
