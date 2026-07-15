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
}
