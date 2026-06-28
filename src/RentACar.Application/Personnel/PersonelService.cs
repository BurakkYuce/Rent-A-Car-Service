using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

/// <summary>
/// Personel master iş mantığı (roadmap C1): doğrulama + Kod benzersizliği + CRUD. PII (TcKimlik, Maas)
/// <see cref="ISecretProtector"/> ile at-rest ŞİFRELENİR (D1 deseni). Hassas (TC/maaş) → tüm işlemler
/// <see cref="Permission.ManageUsers"/> (yalnız Admin). Güncellemede PII alanı BOŞ ise mevcut korunur.
/// Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class PersonelService(
    IPersonelRepository repository, ICurrentUser currentUser, ISecretProtector secrets)
{
    private readonly IPersonelRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ISecretProtector _secrets = secrets;

    public async Task<IReadOnlyList<Personel>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _repository.ListAsync(ct);
    }

    /// <summary>Detay (PII çözülmüş) — düzenleme formu için.</summary>
    public async Task<PersonelDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var r = await _repository.FindAsync(id, ct);
        if (r is null) return null;
        return new PersonelDetail(
            r.Id, r.Kod, r.Ad, r.Soyad, _secrets.Unprotect(r.TcKimlikEnc),
            r.IseGiris, r.IseCikis, r.SurucuBelgeNo, ParseMaas(_secrets.Unprotect(r.MaasEnc)), r.Sube, r.Aktif);
    }

    public async Task<Guid> CreateAsync(PersonelInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod!, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' sicilli personel zaten var.");

        var row = new Personel();
        ApplyPlain(row, n);
        row.TcKimlikEnc = _secrets.Protect(n.TcKimlik);
        row.MaasEnc = _secrets.Protect(MaasToText(n.Maas));
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, PersonelInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod!, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' sicilli personel zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            ApplyPlain(row, n);
            // PII: dolu ise şifrele+güncelle; boş ise mevcut cipher KORUNUR.
            if (!string.IsNullOrWhiteSpace(n.TcKimlik)) row.TcKimlikEnc = _secrets.Protect(n.TcKimlik);
            if (n.Maas is not null) row.MaasEnc = _secrets.Protect(MaasToText(n.Maas));
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(PersonelInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Sicil (kod) zorunludur.");
        if (n.Kod!.Length > 32) throw new ValidationException("Sicil en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ad zorunludur.");
        if (string.IsNullOrWhiteSpace(n.Soyad)) throw new ValidationException("Soyad zorunludur.");
        if (n.Maas is < 0m) throw new ValidationException("Maaş negatif olamaz.");
        if (n.IseCikis is { } c && n.IseGiris is { } g && c < g)
            throw new ValidationException("İşten çıkış, işe giriş tarihinden önce olamaz.");
    }

    private static PersonelInput Normalize(PersonelInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Soyad = (input.Soyad ?? string.Empty).Trim(),
        TcKimlik = TrimOrNull(input.TcKimlik),
        IseGiris = input.IseGiris,
        IseCikis = input.IseCikis,
        SurucuBelgeNo = TrimOrNull(input.SurucuBelgeNo),
        Maas = input.Maas,
        Sube = TrimOrNull(input.Sube),
        Aktif = input.Aktif
    };

    private static void ApplyPlain(Personel row, PersonelInput n)
    {
        row.Kod = n.Kod!;
        row.Ad = n.Ad!;
        row.Soyad = n.Soyad!;
        row.IseGiris = n.IseGiris;
        row.IseCikis = n.IseCikis;
        row.SurucuBelgeNo = n.SurucuBelgeNo;
        row.Sube = n.Sube;
        row.Aktif = n.Aktif;
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? MaasToText(decimal? m) => m?.ToString(CultureInfo.InvariantCulture);
    private static decimal? ParseMaas(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
}
