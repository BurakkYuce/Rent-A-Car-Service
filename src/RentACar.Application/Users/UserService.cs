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
            .Select(u => new UserListItem(u.Id, u.UserName, u.DisplayName, u.Rol, u.IsActive, u.AtanmisSube))
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
            IsActive = true,
            AtanmisSube = string.IsNullOrWhiteSpace(input.AtanmisSube) ? null : input.AtanmisSube.Trim()
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

    /// <summary>
    /// FAZ-83 — Kullanıcının KENDİ parolasını, eski parolasını doğrulayarak değiştirmesi.
    ///
    /// <para><b>Admin guard'ı BİLİNÇLİ OLARAK YOK</b> (<c>RequireAdmin()</c> çağrılmaz): herhangi
    /// rol kendi parolasını değiştirebilmeli. Bu, <see cref="ResetPasswordAsync"/>'ten farklı bir
    /// yetki modelidir ve tam bu yüzden kimlik <b>ASLA parametreden alınmaz</b> —
    /// <see cref="ICurrentUser.UserId"/>'den okunur. Aksi hâlde giriş yapmış herhangi biri, başka
    /// bir kullanıcının id'sini geçirerek onun parolasını değiştirebilirdi (yetki yükseltme).
    /// Bu yüzden metodun <c>id</c> parametresi YOKTUR ve olmamalıdır.</para>
    ///
    /// <para>Tenant sınırı ayrıca RLS + global query filter ile korunur: başka tenant'ın
    /// kullanıcısı <c>FindAsync</c> ile bulunamaz.</para>
    /// </summary>
    public async Task<bool> ChangeOwnPasswordAsync(string eskiSifre, string yeniSifre, CancellationToken ct = default)
    {
        if (_currentUser.UserId is not { } uid)
            throw new ValidationException("Oturum bulunamadı.");
        if (string.IsNullOrWhiteSpace(yeniSifre) || yeniSifre.Length < 6)
            throw new ValidationException("Parola en az 6 karakter olmalıdır.");

        var user = await _repository.FindAsync(uid, ct)
            ?? throw new ValidationException("Oturum bulunamadı.");

        // Eski parola doğrulaması: oturumu çalınmış bir tarayıcının parolayı sessizce
        // değiştirmesini zorlaştıran tek kontrol bu.
        if (!_hasher.Verify(user.PasswordHash, eskiSifre))
            throw new ValidationException("Mevcut parola hatalı.");

        var hash = _hasher.Hash(yeniSifre);
        return await _repository.UpdateAsync(uid, u => u.PasswordHash = hash, ct);
    }
}
