using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>Sigorta/MTV/Muayene CRUD + doğrulama (araç zorunlu, tarih tutarlılığı).</summary>
public sealed class RegulationService(IRegulationRepository repository)
{
    private readonly IRegulationRepository _repository = repository;

    public Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default)
        => _repository.ListInsuranceAsync(ct);
    public Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default)
        => _repository.ListMtvAsync(ct);
    public Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default)
        => _repository.ListInspectionAsync(ct);

    public async Task<Guid> AddInsuranceAsync(
        Guid vehicleId, InsuranceType tip, DateTimeOffset baslangic, DateTimeOffset bitis,
        decimal prim, string? policeNo, string? firma, string? acenta, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (bitis <= baslangic) throw new ValidationException("Bitiş başlangıçtan sonra olmalıdır.");
        if (prim < 0) throw new ValidationException("Prim negatif olamaz.");
        var p = new InsurancePolicy
        {
            VehicleId = vehicleId, Tip = tip, Baslangic = baslangic, Bitis = bitis,
            Prim = prim, PoliceNo = Trim(policeNo), Firma = Trim(firma), Acenta = Trim(acenta)
        };
        await _repository.AddInsuranceAsync(p, ct);
        return p.Id;
    }

    public async Task<Guid> AddMtvAsync(
        Guid vehicleId, string donem, decimal tutar, DateTimeOffset vade, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (string.IsNullOrWhiteSpace(donem)) throw new ValidationException("Dönem zorunludur.");
        if (tutar < 0) throw new ValidationException("Tutar negatif olamaz.");
        var m = new MtvRecord { VehicleId = vehicleId, Donem = donem.Trim(), Tutar = tutar, Vade = vade };
        await _repository.AddMtvAsync(m, ct);
        return m.Id;
    }

    public async Task<Guid> AddInspectionAsync(
        Guid vehicleId, DateTimeOffset muayeneTarihi, DateTimeOffset bitis, decimal ucret, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (bitis <= muayeneTarihi) throw new ValidationException("Bitiş muayene tarihinden sonra olmalıdır.");
        if (ucret < 0) throw new ValidationException("Ücret negatif olamaz.");
        var i = new InspectionRecord { VehicleId = vehicleId, MuayeneTarihi = muayeneTarihi, Bitis = bitis, Ucret = ucret };
        await _repository.AddInspectionAsync(i, ct);
        return i.Id;
    }

    private static void RequireVehicle(Guid vehicleId)
    {
        if (vehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
