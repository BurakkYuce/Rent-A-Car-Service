using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CoverageProducts;

/// <summary>
/// Sigorta/ek hizmet ürün kataloğu master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma
/// operasyonel fiyat yapılandırmasıdır → <see cref="Permission.OperationsWrite"/>. Açılır liste/fiyat
/// motoru okuması (<see cref="ListActiveAsync"/>) yetkisizdir. Tenant izolasyonu/audit alt katmanda
/// otomatik. Saf fiyat-tanım — deftere kayıt postlamaz.
/// </summary>
public sealed class CoverageProductService(ICoverageProductRepository repository, ICurrentUser currentUser)
{
    private readonly ICoverageProductRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<CoverageProduct>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Fiyat motoru / açılır liste kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<CoverageProduct>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<CoverageProduct?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CoverageProductInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu sigorta ürünü zaten var.");

        var row = new CoverageProduct();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, CoverageProductInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu sigorta ürünü zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(CoverageProductInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Ürün kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Ürün kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ürün adı zorunludur.");
        if (n.GunlukUcret is < 0m) throw new ValidationException("Günlük ücret negatif olamaz.");
        if (n.KdvOrani is < 0m or > 100m) throw new ValidationException("KDV oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.MaxGun is < 0) throw new ValidationException("Max gün negatif olamaz.");
    }

    private static CoverageProductInput Normalize(CoverageProductInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        AdEn = TrimOrNull(input.AdEn),
        Aciklama = TrimOrNull(input.Aciklama),
        Tur = input.Tur,
        GunlukUcret = input.GunlukUcret,
        KdvOrani = input.KdvOrani,
        MaxGun = input.MaxGun,
        Doviz = string.IsNullOrWhiteSpace(input.Doviz) ? null : input.Doviz.Trim().ToUpperInvariant(),
        Zorunlu = input.Zorunlu,
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(CoverageProduct row, CoverageProductInput n)
    {
        row.Kod = n.Kod;
        row.Ad = n.Ad;
        row.AdEn = n.AdEn;
        row.Aciklama = n.Aciklama;
        row.Tur = n.Tur;
        row.GunlukUcret = n.GunlukUcret;
        row.KdvOrani = n.KdvOrani;
        row.MaxGun = n.MaxGun;
        row.Doviz = n.Doviz;
        row.Zorunlu = n.Zorunlu;
        row.Aktif = n.Aktif;
    }
}
