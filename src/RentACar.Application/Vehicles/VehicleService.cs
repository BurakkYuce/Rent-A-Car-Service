using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

/// <summary>
/// Araç iş mantığı: doğrulama (plaka zorunlu + tenant içinde benzersiz) + CRUD.
/// Tenant izolasyonu ve audit alt katmanda (DbContext filter + RLS + interceptor)
/// otomatik uygulanır — bu servis tenant'tan habersizdir.
/// </summary>
public sealed class VehicleService(IVehicleRepository repository)
{
    private readonly IVehicleRepository _repository = repository;

    public Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

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
