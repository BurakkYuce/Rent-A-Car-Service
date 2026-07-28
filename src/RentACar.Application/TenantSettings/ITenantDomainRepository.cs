namespace RentACar.Application.TenantSettings;

/// <summary>PR-2: tenant'ın public-site host eşlemesi (TenantDomain, platform tablosu — RLS yok).</summary>
public interface ITenantDomainRepository
{
    /// <summary>"Sitemi Aç": tenant için `{Tenant.Code}.rentpro.com` host'unu Active olarak ekler
    /// (idempotent — zaten varsa no-op).</summary>
    Task EnsureSubdomainAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Ayarlar ekranında göstermek için tenant'ın aktif subdomain host'u (yoksa null).</summary>
    Task<string?> GetActiveHostAsync(Guid tenantId, CancellationToken ct = default);
}
