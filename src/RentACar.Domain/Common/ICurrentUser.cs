namespace RentACar.Domain.Common;

/// <summary>
/// Geçerli kullanıcının kimliği — AuditLog'un "kim" alanı için.
/// Web'de claim'den, testlerde test double'ından beslenir.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
}
