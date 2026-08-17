using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Users;

/// <summary>Liste satırı (UI): kullanıcı başına istisnalar.</summary>
public sealed record KullaniciIzinSatiri(Guid UserId, string Izin, bool Ver, string? Tanimlayan, DateTimeOffset TarihUtc);

public interface IKullaniciIzinRepository
{
    Task<IReadOnlyList<KullaniciIzinSatiri>> ListAsync(CancellationToken ct = default);
    /// <summary>Upsert (TenantId repo'da damgalanır; (tenant,user,izin) unique — son yazan kazanır).</summary>
    Task UpsertAsync(Guid userId, string izin, bool ver, string? tanimlayan, CancellationToken ct = default);
    Task<bool> RemoveAsync(Guid userId, string izin, CancellationToken ct = default);
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
public sealed class KullaniciIzinService(
    IKullaniciIzinRepository repository, IUserRepository users, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<KullaniciIzinSatiri>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return repository.ListAsync(ct);
    }

    public async Task SetAsync(Guid userId, string izinAdi, bool ver, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);

        if (!Enum.TryParse<Permission>(izinAdi, ignoreCase: false, out var izin))
            throw new ValidationException("Geçersiz izin adı.");
        if (currentUser.UserId == userId)
            throw new ValidationException("Kendi izin istisnanızı değiştiremezsiniz (başka bir yönetici yapmalı).");

        var hedef = await users.FindAsync(userId, ct)
            ?? throw new ValidationException("Kullanıcı bulunamadı.");

        // Kilitlenme kemeri: Admin'den ManageUsers alınamaz. (Admin'e istisna vermenin de anlamı
        // yok — matris zaten hepsini veriyor — ama zararsız; yalnız tehlikeli yön engellenir.)
        if (!ver && izin == Permission.ManageUsers && hedef.Rol == UserRole.Admin)
            throw new ValidationException("Admin rolündeki kullanıcıdan kullanıcı-yönetimi yetkisi alınamaz.");

        await repository.UpsertAsync(userId, izin.ToString(), ver, currentUser.UserName, ct);
    }

    public async Task<bool> RemoveAsync(Guid userId, string izinAdi, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        if (currentUser.UserId == userId)
            throw new ValidationException("Kendi izin istisnanızı değiştiremezsiniz (başka bir yönetici yapmalı).");
        return await repository.RemoveAsync(userId, izinAdi, ct);
    }
}
