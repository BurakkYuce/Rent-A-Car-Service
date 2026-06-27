using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Users;

/// <summary>
/// Kullanıcı yönetimi — yalnız Admin. Her işlem geçerli kullanıcının rolünü kontrol eder
/// (servis-katmanı yetki guard'ı → test edilebilir + web [Authorize] ile çift savunma).
/// Kullanıcılar geçerli tenant'a kapsamlıdır; cross-tenant oluşturma RLS ile de engellenir.
/// </summary>
public sealed class UserService(IUserRepository repository, IPasswordHasher hasher, ICurrentUser currentUser)
{
    private readonly IUserRepository _repository = repository;
    private readonly IPasswordHasher _hasher = hasher;
    private readonly ICurrentUser _currentUser = currentUser;

    private void RequireAdmin() => PermissionGuard.Require(_currentUser, Permission.ManageUsers);

    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        var users = await _repository.ListAsync(ct);
        return users
            .Select(u => new UserListItem(u.Id, u.UserName, u.DisplayName, u.Rol, u.IsActive))
            .ToList();
    }

    public async Task<Guid> CreateAsync(UserInput input, CancellationToken ct = default)
    {
        RequireAdmin();
        var userName = (input.UserName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(userName)) throw new ValidationException("Kullanıcı adı zorunludur.");
        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length < 6)
            throw new ValidationException("Parola en az 6 karakter olmalıdır.");
        if (await _repository.UserNameExistsAsync(userName, ct))
            throw new ValidationException("Bu kullanıcı adı zaten kullanımda.");

        var user = new User
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? userName : input.DisplayName.Trim(),
            Rol = input.Rol,
            IsActive = true
        };
        user.PasswordHash = _hasher.Hash(input.Password);
        await _repository.CreateAsync(user, ct);
        return user.Id;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        RequireAdmin();
        // Kendini pasifleştirme/kilitlenme önlemi.
        if (!active && id == _currentUser.UserId)
            throw new ValidationException("Kendi hesabınızı pasifleştiremezsiniz.");
        return await _repository.UpdateAsync(id, u => u.IsActive = active, ct);
    }

    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        RequireAdmin();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new ValidationException("Parola en az 6 karakter olmalıdır.");
        var hash = _hasher.Hash(newPassword);
        return await _repository.UpdateAsync(id, u => u.PasswordHash = hash, ct);
    }
}
