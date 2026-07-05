using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Locations;

/// <summary>
/// Ofis/Lokasyon master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. Açılır liste okuması
/// (<see cref="ListActiveAsync"/>) yetkisizdir (rezervasyon/teklif/kira formu çağırır).
/// Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class LocationService(ILocationRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly ILocationRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private const string CK = "locations";

    public Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CK, () => _repository.ListAsync(ct), ct);

    /// <summary>Form açılır listesi kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public async Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

    public Task<Location?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(LocationInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ofis zaten var.");

        var loc = new Location();
        Apply(loc, n);
        await _repository.CreateAsync(loc, ct);
        _cache.Invalidate(CK);
        return loc.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, LocationInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ofis zaten var.");

        var ok = await _repository.UpdateAsync(id, loc =>
        {
            Apply(loc, n);
            loc.UpdatedAtUtc = DateTimeOffset.UtcNow;
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

    private static void Validate(LocationInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Ofis kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Ofis kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ofis adı zorunludur.");
    }

    private static LocationInput Normalize(LocationInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Adres = Trim(input.Adres),
        Telefon = Trim(input.Telefon),
        Eposta = Trim(input.Eposta),
        CalismaSaatleri = Trim(input.CalismaSaatleri),
        TeslimUcreti = input.TeslimUcreti,
        Sube = Trim(input.Sube),
        Aktif = input.Aktif
    };

    private static void Apply(Location loc, LocationInput n)
    {
        loc.Kod = n.Kod;
        loc.Ad = n.Ad;
        loc.Adres = n.Adres;
        loc.Telefon = n.Telefon;
        loc.Eposta = n.Eposta;
        loc.CalismaSaatleri = n.CalismaSaatleri;
        loc.TeslimUcreti = n.TeslimUcreti;
        loc.Sube = n.Sube;
        loc.Aktif = n.Aktif;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
