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

    Task<MtvRecord?> FindMtvAsync(Guid id, CancellationToken ct = default);

    /// <summary>MTV ödeme (roadmap J1): tek transaction'da Odendi=true + DENGELİ defter kümesi. SourceId=mtvId
    /// deterministik → çift-ödeme idempotency index ile reddedilir (eşzamanlı yarış güvenli).</summary>
    Task PostMtvOdemeAsync(Guid mtvId, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    Task<InspectionRecord?> FindInspectionAsync(Guid id, CancellationToken ct = default);

    /// <summary>Muayene ödeme (roadmap J2): tek transaction'da Odendi=true + Ceza güncelle + DENGELİ defter
    /// kümesi. SourceId=inspectionId deterministik → çift-ödeme idempotency index ile reddedilir.</summary>
    Task PostMuayeneOdemeAsync(Guid inspectionId, decimal ceza, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    Task<InsurancePolicy?> FindInsuranceAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sigorta ödeme (roadmap J3): tek transaction'da Odendi=true + ZeyilPrim güncelle + DENGELİ defter
    /// kümesi. SourceId=policyId deterministik → çift-ödeme idempotency index ile reddedilir.</summary>
    Task PostSigortaOdemeAsync(Guid policyId, decimal zeyilPrim, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>Birleşik vade kaynakları: sigorta(Bitiş) + ödenmemiş MTV(Vade) + muayene(Bitiş).</summary>
    Task<IReadOnlyList<VadeSource>> GetVadeSourcesAsync(CancellationToken ct = default);
}
