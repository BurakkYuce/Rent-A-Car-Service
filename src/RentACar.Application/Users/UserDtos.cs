using RentACar.Domain.Enums;

namespace RentACar.Application.Users;

/// <summary>Kullanıcı liste satırı (parola hash'i ASLA dışarı verilmez).</summary>
public sealed record UserListItem(Guid Id, string UserName, string DisplayName, UserRole Rol, bool IsActive);

/// <summary>Yeni kullanıcı girdisi (Admin tarafından).</summary>
public sealed class UserInput
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Rol { get; set; } = UserRole.Operator;
    public string Password { get; set; } = string.Empty;
}
