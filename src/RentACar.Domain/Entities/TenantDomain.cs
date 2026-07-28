namespace RentACar.Domain.Entities;

public enum TenantDomainKind
{
    Subdomain = 0,
    Custom = 1
}

public enum TenantDomainStatus
{
    Active = 0,
    PendingVerification = 1,
    Failed = 2
}

/// <summary>
/// Host→tenant eşlemesi (public site, PR-0). PLATFORM tablosu — <see cref="Tenant"/>/<see cref="User"/> gibi
/// RLS/tenant filtresi YOK (host'tan tenant'ı bulmadan ÖNCE tenant bağlamı zaten yok — bkz. PublicTenantResolver).
/// Subdomain (`{Tenant.Code}.rentpro.com`) "Sitemi Aç" ile anında + Status=Active oluşturulur; Custom domain
/// (kendi alan adı) DNS doğrulaması bekleyen PendingVerification ile başlar (roadmap PR-8).
/// </summary>
public class TenantDomain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Lower-case host, örn "yucerent.rentpro.com" veya "www.musteri-firma.com".</summary>
    public string Host { get; set; } = string.Empty;

    public TenantDomainKind Kind { get; set; }
    public TenantDomainStatus Status { get; set; }

    /// <summary>Yalnız Kind=Custom'da kullanılır (DNS TXT doğrulama token'ı, roadmap PR-8).</summary>
    public string? VerificationToken { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? VerifiedAtUtc { get; set; }
}
