using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Program kullanıcısı. Bir tenant'a bağlıdır (<see cref="TenantId"/>). PLATFORM/auth
/// tablosudur: okuma (login bootstrap) tenant GUC'u olmadan da çalışsın diye SELECT açık;
/// YAZMA (oluşturma/güncelleme) RLS ile tenant'a kısıtlıdır (WITH CHECK). Owner (migrator/
/// seeder) RLS'i bypass eder (ENABLE, FORCE değil) → cross-tenant seed mümkün.
/// İş verisi izolasyonu (Vehicle/AuditLog/Ledger) tam FORCE RLS ile korunur.
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

    /// <summary>Sabit rol (yetki). Yeni kullanıcılar varsayılan en düşük yetki: Operator.</summary>
    public UserRole Rol { get; set; } = UserRole.Operator;

    /// <summary>
    /// Atanmış şube (Sube metni). Operatör YALNIZ bu şubenin kayıtlarını görür; boşsa veya
    /// rol Admin/Yönetici/Muhasebe ise tüm şubeler görünür. (Master tablo değil — mevcut Sube metni.)
    /// </summary>
    public string? AtanmisSube { get; set; }
    /// <summary>Atanmış şube FK (Branch master, roadmap F1; metin korunur).</summary>
    public Guid? AtanmisSubeId { get; set; }
}
