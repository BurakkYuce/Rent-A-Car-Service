using RentACar.Domain.Entities;

namespace RentACar.Web.Api.ServiceInsurance;

// F9.1 — sigorta / MTV / muayene / vade DTOs. Amounts are native (policy currency for sigorta, TRY for MTV/muayene).
// `kalan` and the FAZ-15 value fields are INFORMATION (never ledger); ledger writes happen only through the /odeme
// endpoints.

public sealed record InsurancePolicyRow(Guid Id, Guid VehicleId, string Plaka, string Tip, DateTimeOffset Baslangic,
    DateTimeOffset Bitis, decimal Prim, decimal ZeyilPrim, string Doviz, string? PoliceNo, string? Firma, string? Acenta,
    decimal? AracDegeri, decimal? ImmDegeri, decimal? AksesuarDegeri, decimal Kalan, bool Odendi)
{
    public static InsurancePolicyRow From(InsurancePolicy p, string plaka) => new(p.Id, p.VehicleId, plaka, p.Tip.ToString(),
        p.Baslangic, p.Bitis, p.Prim, p.ZeyilPrim, p.Currency, p.PoliceNo, p.Firma, p.Acenta, p.AracDegeri, p.ImmDegeri,
        p.AksesuarDegeri, p.Kalan, p.Odendi);
}

public sealed record EndorsementDto(Guid Id, Guid PolicyId, string ZeyilNo, DateTimeOffset Tarih, DateTimeOffset? Tanzim,
    decimal Deger, decimal Brut, decimal Net, decimal FonVergi, string? Tipi, string? Neden)
{
    public static EndorsementDto From(InsurancePolicyZeyil z) => new(z.Id, z.PolicyId, z.ZeyilNo, z.Tarih, z.Tanzim, z.Deger,
        z.Brut, z.Net, z.FonVergi, z.Tipi, z.Neden);
}

/// <summary>Ödemenin defterdeki izi (kaynak: defter satırları, varlık değil).</summary>
public sealed record LedgerPaymentTrace(DateTimeOffset Tarih, decimal Tutar, string Doviz, decimal Kur, decimal TutarBaz,
    string Hesap, Guid? HesapId);

/// <summary>Detail actions the SPA may show (the server re-checks every one).</summary>
public sealed record RegulationActions(bool Odeyebilir, bool Duzenleyebilir);

public sealed record InsurancePolicyDetail(InsurancePolicyRow Police, IReadOnlyList<EndorsementDto> Zeyiller,
    LedgerPaymentTrace? Odeme, RegulationActions Yetkiler);

public sealed record InsurancePolicyRequest(Guid? VehicleId, string? Tip, DateTimeOffset? Baslangic, DateTimeOffset? Bitis,
    decimal? Prim, string? Doviz, string? PoliceNo, string? Firma, string? Acenta, decimal? AracDegeri, decimal? ImmDegeri,
    decimal? AksesuarDegeri);

/// <summary>Sigorta ödemesi. Yapısal idempotency (poliçe başına tek ödeme): başlık gerekmez; ödenmiş poliçe → 409
/// <c>mukerrer</c> + <c>mevcut</c>. <c>kur</c> boş → TRY=1 / döviz poliçede KurCozucu (bulunamazsa 400).</summary>
public sealed record InsurancePaymentRequest(string? Hesap, Guid? HesapId, decimal? ZeyilEkPrim, decimal? Kur);

public sealed record EndorsementRequest(string? ZeyilNo, DateTimeOffset? Tarih, DateTimeOffset? Tanzim, decimal? Deger,
    decimal? Brut, decimal? Net, decimal? FonVergi, string? Tipi, string? Neden);

public sealed record MtvRow(Guid Id, Guid VehicleId, string Plaka, string Donem, decimal Tutar, decimal Kalan,
    DateTimeOffset Vade, bool Odendi, string? Aciklama)
{
    public static MtvRow From(MtvRecord m, string plaka) => new(m.Id, m.VehicleId, plaka, m.Donem, m.Tutar, m.Kalan, m.Vade,
        m.Odendi, m.Aciklama);
}

public sealed record InspectionRow(Guid Id, Guid VehicleId, string Plaka, DateTimeOffset MuayeneTarihi, DateTimeOffset Bitis,
    decimal Ucret, decimal Ceza, decimal Kalan, int? IslemKm, bool Odendi, string? Aciklama)
{
    public static InspectionRow From(InspectionRecord i, string plaka) => new(i.Id, i.VehicleId, plaka, i.MuayeneTarihi,
        i.Bitis, i.Ucret, i.Ceza, i.Kalan, i.IslemKm, i.Odendi, i.Aciklama);
}

/// <summary>Kısmi ödeme satırı (MTV ya da muayene; muayenede <c>ceza</c> dolu).</summary>
public sealed record InstallmentPaymentDto(Guid Id, int Sira, DateTimeOffset Tarih, decimal Tutar, decimal Ceza,
    decimal KalanSonrasi, string Hesap, Guid? HesapId, string? KasaKodu, string? HesapNo, string? EvrakNo,
    string? IslemYapan, string? Aciklama);

public sealed record MtvDetail(MtvRow Mtv, IReadOnlyList<InstallmentPaymentDto> Odemeler, RegulationActions Yetkiler);

public sealed record InspectionDetail(InspectionRow Muayene, IReadOnlyList<InstallmentPaymentDto> Odemeler,
    RegulationActions Yetkiler);

public sealed record MtvRequest(Guid? VehicleId, string? Donem, decimal? Tutar, DateTimeOffset? Vade, string? Aciklama);

public sealed record InspectionRequest(Guid? VehicleId, DateTimeOffset? MuayeneTarihi, DateTimeOffset? Bitis,
    decimal? Ucret, int? IslemKm, string? Aciklama);

/// <summary>
/// MTV / muayene kısmi ödemesi. <c>Idempotency-Key</c> ZORUNLU. <c>tutar</c> boş → kalanın (muayenede kalan + ceza)
/// tamamı. <c>beklenenKalan</c> (isteğe bağlı): ekranın gördüğü kalan; kilit altında farklıysa 409 <c>cakisma</c>
/// (iki sekme / bayat ekran — hiçbir şey yazılmaz).
/// </summary>
public sealed record InstallmentPaymentRequest(string? Hesap, Guid? HesapId, decimal? Tutar, decimal? Ceza,
    DateTimeOffset? OdemeTarihi, decimal? BeklenenKalan, string? EvrakNo, string? IslemYapan, string? KasaKodu,
    string? HesapNo, string? Aciklama);

public sealed record InstallmentPaymentResult(Guid OdemeId, int Sira, decimal Tutar, decimal Kalan, bool Odendi);

public sealed record RegulationOptions(IReadOnlyList<string> Firmalar, IReadOnlyList<string> ZeyilTipleri,
    IReadOnlyList<string> Dovizler, IReadOnlyList<string> SigortaTipleri);

/// <summary>Vade panosu satırı: kova Gecmis / YediGun / OtuzGun / Ileri.</summary>
public sealed record DueItemDto(Guid VehicleId, string Plaka, string Tur, DateTimeOffset Bitis, int KalanGun, string Kova);

public sealed record DueSummary(int Gecmis, int YediGun, int OtuzGun, int Ileri);

public sealed record DueBoard(DueSummary Ozet, Application.Common.Sayfa<DueItemDto> Kalemler);
