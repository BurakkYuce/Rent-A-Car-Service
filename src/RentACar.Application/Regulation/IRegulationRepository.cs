using RentACar.Domain.Entities;

namespace RentACar.Application.Regulation;

/// <summary>
/// Sigorta/MTV/Muayene kalıcılığı (güncellenebilir kayıtlar; mali belge değil).
/// + vade panosu için birleşik bitiş-tarihi kaynakları.
/// </summary>
public interface IRegulationRepository
{
    Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default);

    Task AddInsuranceAsync(InsurancePolicy policy, CancellationToken ct = default);
    Task AddMtvAsync(MtvRecord record, CancellationToken ct = default);
    Task AddInspectionAsync(InspectionRecord record, CancellationToken ct = default);

    /// <summary>Birleşik vade kaynakları: sigorta(Bitiş) + ödenmemiş MTV(Vade) + muayene(Bitiş).</summary>
    Task<IReadOnlyList<VadeSource>> GetVadeSourcesAsync(CancellationToken ct = default);
}
