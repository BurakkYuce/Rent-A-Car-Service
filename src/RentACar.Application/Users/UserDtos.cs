using RentACar.Domain.Enums;

namespace RentACar.Application.Users;

/// <summary>Kullanıcı liste satırı (parola hash'i ASLA dışarı verilmez).</summary>
public sealed record UserListItem(
    Guid Id, string UserName, string DisplayName, UserRole Rol, bool IsActive, string? AtanmisSube);

/// <summary>Yeni kullanıcı girdisi (Admin tarafından).</summary>
public sealed class UserInput
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Rol { get; set; } = UserRole.Operator;
    public string Password { get; set; } = string.Empty;
    /// <summary>Atanmış şube (Sube metni). Operatör için kapsam; boş = tüm şubeler.</summary>
    public string? AtanmisSube { get; set; }
}

/// <summary>Kabul d-sistem-kullanici-09 — var olan kullanıcının rol + atanmış şube güncellemesi (tam değiştirme).</summary>
public sealed class UserUpdateInput
{
    public UserRole Rol { get; set; } = UserRole.Operator;
    /// <summary>Atanmış şube adı (firmanın aktif şubesi; çağıran doğrular). Boş = tüm şubeler.</summary>
    public string? AtanmisSube { get; set; }
    /// <summary>Atanmış şubenin kimliği — metinle BİRLİKTE yazılır (FK yolu; boş şubede FK de temizlenir).</summary>
    public Guid? AtanmisSubeId { get; set; }
}
