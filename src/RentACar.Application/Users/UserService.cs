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

    /// <summary>
    /// F11.1b güvenlik M2 — Admin hesaplarına dokunan işlemler (Admin oluşturma, Admin parolası/durumu) yalnız Admin
    /// ROLÜNE. ManageUsers istisnası verilmiş bir Yönetici aksi halde Admin hesabını ele geçirebiliyordu.
    /// </summary>
    private void RequireAdminRole(string message)
    {
        if (_currentUser.Role != Domain.Enums.UserRole.Admin) throw new NoPermissionException(message);
    }

    /// <summary>Rol kıdemi (güvenlik F2): Admin &gt; Yönetici &gt; Operatör = Muhasebe.</summary>
    private static int Rank(Domain.Enums.UserRole? role) => role switch
    {
        Domain.Enums.UserRole.Admin => 3,
        Domain.Enums.UserRole.Yonetici => 2,
        null => 0,
        _ => 1,
    };

    /// <summary>
    /// Güvenlik F2 — aktör kendi kıdeminden YÜKSEK rol atayamaz ve kendisinden kıdemli kullanıcıyı değiştiremez
    /// (ManageUsers istisnalı Operatör → Yönetici yapamaz; Yönetici → Admin zaten M2 ile kapalı).
    /// </summary>
    private void RequireRankAtMostActor(Domain.Enums.UserRole role, string message)
    {
        if (Rank(role) > Rank(_currentUser.Role)) throw new NoPermissionException(message);
    }

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
        AdminRoleGuard.Require(_currentUser); // güvenlik tur 2 M1: kullanıcı yazmaları yalnız Admin rolü
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
        // F11.1b güvenlik M2: Admin rolü atamak yalnız Admin'e (ManageUsers istisnalı Yönetici kendine Admin açamaz).
        if (input.Rol == Domain.Enums.UserRole.Admin) RequireAdminRole("Admin rolünde kullanıcıyı yalnız Admin oluşturabilir.");
        RequireRankAtMostActor(input.Rol, "Kendi rolünüzden yüksek bir rolde kullanıcı oluşturamazsınız.");
        await FreshActor.RequireAsync(_repository, _currentUser, ct); // güvenlik F1: aktörün güncel rolü DB'den
        user.PasswordHash = _hasher.Hash(input.Password);
        await _repository.CreateAsync(user, new UserAuditEntry("KullaniciOlusturma", $"Rol={user.Rol}"), ct);
        return user.Id;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        RequireAdmin();
        AdminRoleGuard.Require(_currentUser); // güvenlik tur 2 M1
        // Kendini pasifleştirme/kilitlenme önlemi.
        if (!active && id == _currentUser.UserId)
            throw new ValidationException("Kendi hesabınızı pasifleştiremezsiniz.");
        var target = await _repository.FindAsync(id, ct);
        if (target is { Rol: Domain.Enums.UserRole.Admin })
            RequireAdminRole("Admin hesabının durumunu yalnız Admin değiştirebilir.");
        if (target is not null) RequireRankAtMostActor(target.Rol, "Sizden kıdemli roldeki kullanıcının durumunu değiştiremezsiniz.");
        await FreshActor.RequireAsync(_repository, _currentUser, ct); // güvenlik F1
        // F11.1b — son aktif Admin kemeri (M1: sayım repo'da kiracı kilidi ALTINDA, eşzamanlı iki pasifleştirme geçemez).
        return await _repository.UpdateAuditedAsync(id, u => u.IsActive = active,
            new UserAuditEntry(active ? "KullaniciAktif" : "KullaniciPasif"), ct);
    }

    /// <summary>Satır sürümleri (liste/kart <c>surum</c>'u).</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        return _repository.GetVersionsAsync(ct);
    }

    /// <summary>
    /// Kabul d-sistem-kullanici-09 — rol ve atanmış şubeyi değiştirir (tam değiştirme, zorunlu sürüm; bayat → 409).
    /// Kemerler: kendi rolünü ve kendi şubesini değiştiremez (kendini düşürüp son Admin'i yok etme, kapsamını genişletme
    /// ve kilitlenme yolları kapalı — güvenlik F2); kendi kıdeminden yüksek rol veremez ve kıdemlisini değiştiremez;
    /// Admin hesabına dokunmak ve Admin rolü vermek yalnız Admin ROLÜNE (M2); son aktif Admin'in rolü repo'da kiracı
    /// kilidi altında da korunur. Şubenin varlığı/aktifliği çağıranda doğrulanır (ad + kimlik birlikte).
    /// <para>Oturum: güncelleme hedefin oturum damgasını yeniler (repo) → hedefin açık oturumu bir sonraki istekte düşer
    /// ve yeniden girişte yeni rol/şube geçerli olur (güvenlik F1).</para>
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, UserUpdateInput input, string expectedVersion, CancellationToken ct = default)
    {
        RequireAdmin();
        AdminRoleGuard.Require(_currentUser); // güvenlik tur 2 M1
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        var branch = string.IsNullOrWhiteSpace(input.AtanmisSube) ? null : input.AtanmisSube.Trim();
        if (_currentUser.UserId is { } self && self == id)
        {
            if (current.Rol != input.Rol)
                throw new ValidationException("Kendi rolünüzü değiştiremezsiniz; başka bir Admin değiştirmelidir.", "rol");
            if (!string.Equals(current.AtanmisSube?.Trim(), branch, StringComparison.Ordinal))
                throw new ValidationException("Kendi şubenizi değiştiremezsiniz; başka bir yönetici değiştirmelidir.", "atanmisSube");
        }
        if (current.Rol == Domain.Enums.UserRole.Admin)
            RequireAdminRole("Admin hesabını yalnız Admin değiştirebilir.");
        if (input.Rol == Domain.Enums.UserRole.Admin && current.Rol != Domain.Enums.UserRole.Admin)
            RequireAdminRole("Admin rolünü yalnız Admin verebilir.");
        RequireRankAtMostActor(current.Rol, "Sizden kıdemli roldeki kullanıcıyı değiştiremezsiniz.");
        RequireRankAtMostActor(input.Rol, "Kendi rolünüzden yüksek bir rol atayamazsınız.");
        await FreshActor.RequireAsync(_repository, _currentUser, ct); // güvenlik F1: aktörün güncel rolü DB'den

        var branchId = branch is null ? null : input.AtanmisSubeId;
        return await _repository.UpdateAuditedAsync(id, expectedVersion, u =>
        {
            u.Rol = input.Rol;
            u.AtanmisSube = branch;
            u.AtanmisSubeId = branchId;
        }, new UserAuditEntry("KullaniciGuncelleme",
            $"Rol={current.Rol}->{input.Rol}; Sube={current.AtanmisSube ?? "-"}->{branch ?? "-"}"), ct);
    }

    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        RequireAdmin();
        AdminRoleGuard.Require(_currentUser); // güvenlik tur 2 M1
        // F11.2b güvenlik M1: yönetici sıfırlaması KENDİ hesabına uygulanmaz — kendi parolası eski parola doğrulaması
        // ve giriş hız sınırıyla ChangeOwnPasswordAsync'ten değişir (aksi hâlde oturumu ele geçiren eski parolayı
        // bilmeden parolayı değiştirip hesabı kalıcı alırdı).
        if (_currentUser.UserId is { } self && self == id)
            throw new ValidationException("Kendi parolanızı Profil > Parola Değiştir'den değiştirin.", "sifre");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new ValidationException("Parola en az 6 karakter olmalıdır.");
        var target = await _repository.FindAsync(id, ct);
        if (target is { Rol: Domain.Enums.UserRole.Admin })
            RequireAdminRole("Admin hesabının parolasını yalnız Admin sıfırlayabilir.");
        if (target is not null) RequireRankAtMostActor(target.Rol, "Sizden kıdemli roldeki kullanıcının parolasını sıfırlayamazsınız.");
        await FreshActor.RequireAsync(_repository, _currentUser, ct); // güvenlik F1
        var hash = _hasher.Hash(newPassword);
        return await _repository.UpdateAuditedAsync(id, u => u.PasswordHash = hash, new UserAuditEntry("ParolaSifirlama"), ct);
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
    public async Task<bool> ChangeOwnPasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
    {
        if (_currentUser.UserId is not { } uid)
            throw new ValidationException("Oturum bulunamadı.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new ValidationException("Parola en az 6 karakter olmalıdır.");

        var user = await _repository.FindAsync(uid, ct)
            ?? throw new ValidationException("Oturum bulunamadı.");

        // Eski parola doğrulaması: oturumu çalınmış bir tarayıcının parolayı sessizce
        // değiştirmesini zorlaştıran tek kontrol bu.
        if (!_hasher.Verify(user.PasswordHash, oldPassword))
            throw new ValidationException("Mevcut parola hatalı.");

        var hash = _hasher.Hash(newPassword);
        return await _repository.UpdateAuditedAsync(uid, u => u.PasswordHash = hash, new UserAuditEntry("KendiParolasiniDegistirme"), ct);
    }
}
