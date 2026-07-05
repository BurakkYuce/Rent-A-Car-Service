using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Brands;

/// <summary>
/// Marka master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/>
/// (araç formu açılır liste kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class BrandService(IBrandRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly IBrandRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private const string CK = "brands";

    public Task<IReadOnlyList<Brand>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CK, () => _repository.ListAsync(ct), ct);

    public async Task<IReadOnlyList<Brand>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

    public Task<Brand?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(BrandInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu marka zaten var.");

        var brand = new Brand();
        Apply(brand, n);
        await _repository.CreateAsync(brand, ct);
        _cache.Invalidate(CK);
        return brand.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BrandInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu marka zaten var.");

        var ok = await _repository.UpdateAsync(id, brand =>
        {
            Apply(brand, n);
            brand.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var ok = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    private static void Validate(BrandInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Marka kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Marka kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Marka adı zorunludur.");
    }

    private static BrandInput Normalize(BrandInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(Brand brand, BrandInput n)
    {
        brand.Kod = n.Kod;
        brand.Ad = n.Ad;
        brand.Aktif = n.Aktif;
    }
}
