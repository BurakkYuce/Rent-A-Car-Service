using RentACar.Domain.Entities;

namespace RentACar.Application.TenantSettings;

/// <summary>PR-2/PR-5: tenant'ın public-site host eşlemesi (TenantDomain, platform tablosu — RLS yok).</summary>
public interface ITenantDomainRepository
{
    /// <summary>"Sitemi Aç": tenant için `{Tenant.Code}.rentpro.com` host'unu Active olarak ekler
    /// (idempotent — zaten varsa no-op).</summary>
    Task EnsureSubdomainAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Ayarlar ekranında göstermek için tenant'ın aktif subdomain host'u (yoksa null).</summary>
    Task<string?> GetActiveHostAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>PR-5: özel domain ekler — idempotent (aynı host zaten bu tenant'taysa mevcut satırı döner),
    /// `Kind=Custom, Status=PendingVerification`. Tenant başına açık (Pending) özel domain sayısı 2 ile
    /// sınırlıdır (ACME kota-kötüye-kullanım koruması) — aşılırsa `ValidationException`. Host platform-
    /// genelinde benzersizdir (`TenantDomainConfig.HasIndex(x => x.Host).IsUnique()`) — başka bir tenant'a
    /// aitse ihlal `ValidationException`'a çevrilir (domain hijack koruması).</summary>
    Task<TenantDomain> AddCustomAsync(Guid tenantId, string host, CancellationToken ct = default);

    /// <summary>PR-5: Caddy `on_demand_tls` ask-endpoint'i için — Kind/Status FARK ETMEKSİZİN host
    /// `TenantDomains`'te kayıtlı mı (bilinmeyen host'lara sertifika çıkarılmasın).</summary>
    Task<bool> ExistsAsync(string host, CancellationToken ct = default);

    /// <summary>PR-5: `PendingVerification` bir satırı ilk başarılı çözümlemede `Active`+`VerifiedAtUtc`
    /// olarak işaretler (kendi-kendini-doğrulama — bkz. PublicTenantResolver).</summary>
    Task MarkVerifiedAsync(Guid id, CancellationToken ct = default);

    /// <summary>PR-5: Ayarlar ekranındaki domain listesi (Aktif/Doğrulama Bekliyor/Başarısız rozetleriyle).</summary>
    Task<IReadOnlyList<TenantDomain>> ListAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>PR-5 ACME kota-kötüye-kullanım koruması: `olderThan`'dan ÖNCE oluşturulmuş, hâlâ
    /// `PendingVerification` olan TÜM tenant'lardaki özel domain satırlarını TEK SQL UPDATE ile `Failed`'e
    /// çeker (platform tablosu — tenant-loop GEREKMEZ, RLS yok). Etkilenen satır sayısını döner.</summary>
    Task<int> ExpireOldPendingCustomDomainsAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
