namespace RentACar.Domain.Enums;

/// <summary>
/// Aracın FİLO yaşam döngüsü statüsü — operasyonel <see cref="VehicleStatus"/>'tan (Boş/Kirada/
/// Serviste…) AYRIDIR. Canlı TürevRent'in "Araç Status" kümesi: aracın mülkiyet/tedarik durumu.
/// Nullable: mevcut kayıtlar etkilenmesin diye opsiyonel (girilmemiş = bilinmiyor).
/// </summary>
public enum FiloStatus
{
    /// <summary>0 KM stok (yeni, henüz havuza girmemiş).</summary>
    SifirKmStok = 0,
    /// <summary>Havuz (kiralanabilir aktif filo).</summary>
    Havuz = 1,
    /// <summary>Tahsis (belirli müşteri/uzun döneme ayrılmış).</summary>
    Tahsis = 2,
    /// <summary>USK — uzun süreli kiralama.</summary>
    Usk = 3,
    /// <summary>KSK — kısa süreli kiralama.</summary>
    Ksk = 4,
    /// <summary>2. el satış (elden çıkarılıyor).</summary>
    IkinciElSatis = 5,
    /// <summary>Sipariş (tedarik edilecek, henüz teslim alınmamış).</summary>
    Siparis = 6
}
