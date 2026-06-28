namespace RentACar.Domain.Enums;

/// <summary>
/// Sigorta/ek hizmet ürün türü (canlı sigorta_tarife_listesi teminat/paket kalemleri).
/// </summary>
public enum CoverageProductType
{
    Scdw = 0,
    MiniHasar = 1,
    Lcf = 2,
    Cdw = 3,
    Pai = 4,
    /// <summary>İhtiyari Mali Mesuliyet.</summary>
    Imm = 5,
    MuafiyetSigortasi = 6,
    YolYardim = 7,
    GencSurucu = 8,
    MaxGuvence = 9,
    SuperMini = 10,
    /// <summary>Paket sigorta hizmeti (1–6).</summary>
    PaketHizmet = 11,
    /// <summary>Ek KM paketi (1–4).</summary>
    KmPaketi = 12,
    Diger = 99
}
