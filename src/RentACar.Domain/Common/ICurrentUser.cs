using RentACar.Domain.Enums;

namespace RentACar.Domain.Common;

/// <summary>
/// Geçerli kullanıcının kimliği — AuditLog'un "kim" alanı + yetki kararları için.
/// Web'de claim'den, testlerde test double'ından beslenir.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }

    /// <summary>Geçerli kullanıcının rolü (yetki). Anonim/bilinmiyorsa null.</summary>
    UserRole? Role { get; }

    /// <summary>Atanmış şube (Sube metni). Rol bazlı şube kapsamı için; boş = tüm şubeler.</summary>
    string? AssignedBranch { get; }
    /// <summary>Atanmış şube FK'sı (FAZ 5-C1 çift-yazım; C2'de yetki FK-farkındalı olur). Eski oturum/claim'siz → null.</summary>
    Guid? AssignedBranchId { get; }

    // ---- Kullanıcı-bazlı izin istisnaları (2026-08-17). STRING taşınır çünkü Permission enum'u
    // Application katmanında yaşar, Domain ona bakamaz; çözümleme guard'da yapılır (bilinmeyen ad
    // yok sayılır). VARSAYILAN gövdeler boş döner → mevcut tüm implementasyonlar (test double'ları
    // dahil) davranış değiştirmeden derlenir; yalnız claim okuyan kimlikler override eder.

    /// <summary>Role EK verilen izin adları (login'de claim'e yazılır; sonraki girişte etkin).</summary>
    IReadOnlyCollection<string> EkIzinler => Array.Empty<string>();

    /// <summary>Bu kullanıcıdan GERİ ALINAN izin adları. Yasak, matristen ve ek izinden DAİMA üstündür.</summary>
    IReadOnlyCollection<string> YasakIzinler => Array.Empty<string>();
}
