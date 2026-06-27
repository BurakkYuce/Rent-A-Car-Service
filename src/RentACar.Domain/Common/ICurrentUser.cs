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
}
