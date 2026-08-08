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

    /// <summary>
    /// MTV KISMİ ödeme (FAZ-14; roadmap J1'in genişletilmişi). Tek transaction'da:
    /// satır kilidi (FOR UPDATE) → sıra + kalan OKUNUR → <paramref name="posting"/> çağrılır →
    /// ödeme satırı + DENGELİ defter kümesi yazılır → Kalan düşürülür (0'da Odendi=true).
    ///
    /// <para>Kalan ve sıra KİLİDİN ARKASINDA okunur: iki eşzamanlı ödeme aynı kalanı görüp
    /// birlikte bakiyeyi aşamaz ve aynı sırayı alamaz (kayıp-güncelleme yok).</para>
    ///
    /// <para><paramref name="posting"/>: (kalan, sıra) → ödeme satırı + defter kümesi. Aşım gibi
    /// iş kuralları burada ValidationException atarak transaction'ı iptal edebilir.</para>
    /// </summary>
    Task<RegulasyonOdemeSonuc> PostMtvOdemeAsync(
        Guid mtvId,
        Func<decimal, int, (MtvOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default);

    Task<InspectionRecord?> FindInspectionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Muayene KISMİ ödeme (FAZ-14). MTV ile aynı sözleşme; ek olarak ödemedeki ceza kaydın
    /// <c>Ceza</c> toplamına eklenir ve BORCU ARTIRIR: <c>Kalan = Kalan + Ceza − Tutar</c>.
    /// </summary>
    Task<RegulasyonOdemeSonuc> PostMuayeneOdemeAsync(
        Guid inspectionId,
        Func<decimal, int, (MuayeneOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default);

    /// <summary>Bir MTV kaydının ödeme geçmişi (sıraya göre).</summary>
    Task<IReadOnlyList<MtvOdeme>> ListMtvOdemeAsync(Guid mtvId, CancellationToken ct = default);

    /// <summary>Tenant'ın TÜM MTV ödemeleri (liste sayfası için tek sorgu).</summary>
    Task<IReadOnlyList<MtvOdeme>> ListMtvOdemeHepsiAsync(CancellationToken ct = default);

    /// <summary>Bir muayene kaydının ödeme geçmişi (sıraya göre).</summary>
    Task<IReadOnlyList<MuayeneOdeme>> ListMuayeneOdemeAsync(Guid inspectionId, CancellationToken ct = default);

    /// <summary>Tenant'ın TÜM muayene ödemeleri (liste sayfası için tek sorgu).</summary>
    Task<IReadOnlyList<MuayeneOdeme>> ListMuayeneOdemeHepsiAsync(CancellationToken ct = default);

    Task<InsurancePolicy?> FindInsuranceAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sigorta ödeme (roadmap J3): tek transaction'da Odendi=true + ZeyilPrim güncelle + DENGELİ defter
    /// kümesi. SourceId=policyId deterministik → çift-ödeme idempotency index ile reddedilir.</summary>
    Task PostSigortaOdemeAsync(Guid policyId, decimal zeyilPrim, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>Birleşik vade kaynakları: sigorta(Bitiş) + ödenmemiş MTV(Vade) + muayene(Bitiş).</summary>
    Task<IReadOnlyList<VadeSource>> GetVadeSourcesAsync(CancellationToken ct = default);
}

/// <summary>Kısmi ödeme sonucu — çağıran (ve test) ödeme sonrası durumu koddan değil BURADAN okur.</summary>
public sealed record RegulasyonOdemeSonuc(Guid OdemeId, int Sira, decimal Tutar, decimal Kalan, bool Odendi);
