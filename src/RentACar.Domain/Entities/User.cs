namespace RentACar.Domain.Entities;

/// <summary>
/// Program kullanıcısı. Bir tenant'a bağlıdır (<see cref="TenantId"/>) ama PR #1'de
/// PLATFORM/auth tablosu olarak ele alınır: RLS uygulanMAZ (login'in chicken-and-egg
/// sorununu önlemek için), erişim aşama-1'de çözümlenen tenant ile AÇIK filtrelenir.
/// İş verisi izolasyonu (Vehicle/AuditLog/Ledger) tam RLS ile korunur.
/// (User tablosuna RLS = dokümante edilmiş bir sonraki adım.)
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>ASP.NET Core PasswordHasher ile üretilen hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
