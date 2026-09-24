using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.TarifeGruplari;

/// <summary>
/// Tarife grubu master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma → OperationsWrite.
/// Okuma (<see cref="ListActiveAsync"/>) yetkisiz (tarife formu açılır listesi çağırır).
///
/// <para>Şifre DÜZ saklanmaz: <see cref="IPasswordHasher"/> ile tek yönlü özetlenir. Boş şifre
/// güncellemede mevcut özeti KORUR — aksi hâlde her düzenleme kimliği sessizce silerdi.</para>
///
/// <para>Saf tanım — deftere kayıt POSTLAMAZ, fiyat motoru bu tabloyu OKUMAZ.</para>
/// </summary>
public sealed class TarifeGrubuService(
    ITarifeGrubuRepository repository, ICurrentUser currentUser, IPasswordHasher hasher,
    IRowVersionStore? rowVersions = null)
{
    private readonly ITarifeGrubuRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPasswordHasher _hasher = hasher;

    public Task<IReadOnlyList<TarifeGrubu>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<TarifeGrubu>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<TarifeGrubu?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(TarifeGrubuInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife grubu zaten var.");

        var row = new TarifeGrubu();
        Apply(row, n);
        if (!string.IsNullOrWhiteSpace(n.Sifre)) row.SifreHash = _hasher.Hash(n.Sifre!);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, TarifeGrubuInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife grubu zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            // Boş şifre = "değiştirme". Her kaydetmede sıfırlansaydı kimlik sessizce kaybolurdu.
            if (!string.IsNullOrWhiteSpace(n.Sifre)) row.SifreHash = _hasher.Hash(n.Sifre!);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F9.1 — opaque row version for the full-replacement PUT of <c>/api/ui</c>.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersionStoreGuard.Require(rowVersions).GetVersionAsync<TarifeGrubu>(id, ct);

    /// <summary>F9.1 — same rules as <see cref="UpdateAsync"/> under a row lock with a version check. Empty
    /// password keeps the stored hash (same as the Blazor path).</summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, TarifeGrubuInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife grubu zaten var.");
        var hash = string.IsNullOrWhiteSpace(n.Sifre) ? null : _hasher.Hash(n.Sifre!);
        return await RowVersionStoreGuard.Require(rowVersions).UpdateAsync<TarifeGrubu>(id, expectedVersion, row =>
        {
            Apply(row, n);
            if (hash is not null) row.SifreHash = hash;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, $"'{n.Kod}' kodlu tarife grubu zaten var.", ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(TarifeGrubuInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Tarife grubu kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Tarife grubu kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Tarife grubu adı zorunludur.");
        if (n.Oran < 0m) throw new ValidationException("Oran negatif olamaz.");
        if (n.Sifre is { Length: > 0 and < 6 }) throw new ValidationException("Şifre en az 6 karakter olmalıdır.");
    }

    private static TarifeGrubuInput Normalize(TarifeGrubuInput i) => new()
    {
        Kod = (i.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (i.Ad ?? string.Empty).Trim(),
        Oran = i.Oran,
        KullaniciAdi = string.IsNullOrWhiteSpace(i.KullaniciAdi) ? null : i.KullaniciAdi.Trim(),
        // Normalize YENİ nesne kurar; şifreyi buraya eklemeyi atlamak onu sessizce yok ederdi.
        Sifre = string.IsNullOrWhiteSpace(i.Sifre) ? null : i.Sifre.Trim(),
        Aktif = i.Aktif
    };

    private static void Apply(TarifeGrubu r, TarifeGrubuInput n)
    {
        r.Kod = n.Kod;
        r.Ad = n.Ad;
        r.Oran = n.Oran;
        r.KullaniciAdi = n.KullaniciAdi;
        r.Aktif = n.Aktif;
    }
}
