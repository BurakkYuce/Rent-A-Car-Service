using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Users;

/// <summary>Liste satırı (UI): kullanıcı başına istisnalar.</summary>
public sealed record KullaniciIzinSatiri(Guid UserId, string Izin, bool Ver, string? Tanimlayan, DateTimeOffset TarihUtc);

public interface IUserPermissionRepository
{
    Task<IReadOnlyList<KullaniciIzinSatiri>> ListAsync(CancellationToken ct = default);
    /// <summary>Upsert (TenantId repo'da damgalanır; (tenant,user,izin) unique — son yazan kazanır).</summary>
    Task UpsertAsync(Guid userId, string permission, bool give, string? definedBy, CancellationToken ct = default);
    Task<bool> RemoveAsync(Guid userId, string permission, CancellationToken ct = default);
}

/// <summary>
/// Kullanıcı-bazlı izin istisnaları (2026-08-17). Rol matrisinin üstüne tek kullanıcıya ek izin
/// (ver) ya da kısıt (yasak); karar bileşimi <see cref="EffectivePermission"/>'da.
///
/// <para><b>Guard'lar:</b> tüm işlemler <see cref="Permission.ManageUsers"/> ister. KENDİ istisnanı
/// değiştiremezsin — hem kendine izin verme (yetki yükseltme) hem kendini kilitleme aynı kuralla
/// kapanır. Admin ROLÜNDEKİ bir kullanıcıya ManageUsers YASAĞI konamaz — iki admin birbirini
/// kilitleyip tenant'ı yönetimsiz bırakabilirdi (kalan tek çıkış platform konsoluydu).</para>
///
/// <para><b>Etkinleşme:</b> istisnalar login'de claim'e yazılır → değişiklik hedef kullanıcının
/// BİR SONRAKİ girişinde etkinleşir. UI bunu açıkça söyler.</para>
/// </summary>
public sealed class UserPermissionService(
    IUserPermissionRepository repository, IUserRepository users, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<KullaniciIzinSatiri>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return repository.ListAsync(ct);
    }

    public async Task SetAsync(Guid userId, string permissionName, bool give, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);

        if (!Enum.TryParse<Permission>(permissionName, ignoreCase: false, out var permission))
            throw new ValidationException("Geçersiz izin adı.");
        if (currentUser.UserId == userId)
            throw new ValidationException("Kendi izin istisnanızı değiştiremezsiniz (başka bir yönetici yapmalı).");

        var target = await users.FindAsync(userId, ct)
            ?? throw new ValidationException("Kullanıcı bulunamadı.");

        // Kilitlenme kemeri: Admin'den ManageUsers alınamaz. (Admin'e istisna vermenin de anlamı
        // yok — matris zaten hepsini veriyor — ama zararsız; yalnız tehlikeli yön engellenir.)
        if (!give && permission == Permission.ManageUsers && target.Rol == UserRole.Admin)
            throw new ValidationException("Admin rolündeki kullanıcıdan kullanıcı-yönetimi yetkisi alınamaz.");
        // F11.1b güvenlik M2: ManageUsers vermek ve Admin hesabının istisnalarına dokunmak yalnız Admin ROLÜNE.
        if ((give && permission == Permission.ManageUsers) || target.Rol == UserRole.Admin)
            RequireAdminRole();

        await repository.UpsertAsync(userId, permission.ToString(), give, currentUser.UserName, ct);
    }

    public async Task<bool> RemoveAsync(Guid userId, string permissionName, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        if (currentUser.UserId == userId)
            throw new ValidationException("Kendi izin istisnanızı değiştiremezsiniz (başka bir yönetici yapmalı).");
        // F11.1b güvenlik M2: ManageUsers istisnasını kaldırmak (yasağı kaldırmak = yetki iadesi) ve Admin hesabına
        // dokunmak yalnız Admin ROLÜNE.
        if (string.Equals(permissionName, nameof(Permission.ManageUsers), StringComparison.Ordinal)
            || (await users.FindAsync(userId, ct))?.Rol == UserRole.Admin)
            RequireAdminRole();
        return await repository.RemoveAsync(userId, permissionName, ct);
    }

    private void RequireAdminRole()
    {
        if (currentUser.Role != UserRole.Admin)
            throw new NoPermissionException("Bu işlemi (kullanıcı yönetimi yetkisi / Admin hesabı) yalnız Admin rolü yapabilir.");
    }
}
