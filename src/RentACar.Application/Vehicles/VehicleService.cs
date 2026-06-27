using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

/// <summary>
/// Araç iş mantığı: doğrulama (plaka zorunlu + tenant içinde benzersiz) + CRUD.
/// Tenant izolasyonu ve audit alt katmanda (DbContext filter + RLS + interceptor) otomatik.
/// Liste, rol bazlı ŞUBE kapsamıyla filtrelenir (operatör yalnız kendi şubesi).
/// </summary>
public sealed class VehicleService(IVehicleRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(BranchScope.Effective(_currentUser), ct);

    /// <summary>Liste ekranı: arama/filtre + sayfalama. Rol bazlı şube kapsamı zorlanır.</summary>
    public Task<Common.PagedResult<Vehicle>> SearchAsync(VehicleFilter filter, CancellationToken ct = default)
    {
        var scope = BranchScope.Effective(_currentUser);
        if (scope is not null) filter.Sube = scope; // operatör kendi şubesi dışına çıkamaz
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 200) filter.PageSize = 20;
        return _repository.SearchAsync(filter, ct);
    }

    public Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleInput input, CancellationToken ct = default)
    {
        var plaka = Normalize(input.Plaka);
        Validate(plaka, input);

        if (await _repository.PlakaExistsAsync(plaka, excludeId: null, ct))
            throw new DuplicatePlakaException(plaka);

        var vehicle = new Vehicle
        {
            Plaka = plaka,
            Marka = Trim(input.Marka),
            Grup = Trim(input.Grup),
            Sube = Trim(input.Sube),
            Durum = input.Durum,
            Km = input.Km,
            Yakit = input.Yakit
        };

        // Yarış koşulunda DB benzersiz index son güvencedir → repo 23505'i çevirir.
        await _repository.CreateAsync(vehicle, ct);
        return vehicle.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleInput input, CancellationToken ct = default)
    {
        var plaka = Normalize(input.Plaka);
        Validate(plaka, input);

        if (await _repository.PlakaExistsAsync(plaka, excludeId: id, ct))
            throw new DuplicatePlakaException(plaka);

        return await _repository.UpdateAsync(id, v =>
        {
            v.Plaka = plaka;
            v.Marka = Trim(input.Marka);
            v.Grup = Trim(input.Grup);
            v.Sube = Trim(input.Sube);
            v.Durum = input.Durum;
            v.Km = input.Km;
            v.Yakit = input.Yakit;
            v.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => _repository.DeleteAsync(id, ct);

    private static void Validate(string plaka, VehicleInput input)
    {
        if (string.IsNullOrWhiteSpace(plaka))
            throw new ValidationException("Plaka zorunludur.");
        if (input.Km < 0)
            throw new ValidationException("KM negatif olamaz.");
    }

    private static string Normalize(string? plaka)
        => (plaka ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
