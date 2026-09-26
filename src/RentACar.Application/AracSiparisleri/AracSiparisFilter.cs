using RentACar.Domain.Enums;

namespace RentACar.Application.AracSiparisleri;

/// <summary>
/// Araç sipariş liste filtresi (FAZ-17 — canlı `arac_siparis_detay_listesi.aspx` /
/// `arac_siparis_listesi.aspx` paritesi; ikisi de bu tek görünümün alt-kümesidir). Boş alan =
/// kısıt yok. Tarih aralığı <c>SiparisTarihi</c>'ne uygulanır (canlıdaki "Tarih" listesi alanı).
/// </summary>
public sealed class AracSiparisFilter
{
    /// <summary>Tedarikçi cari bağı (canlıdaki Musteri_No seçimi).</summary>
    public Guid? CariId { get; set; }

    /// <summary>
    /// Ad-Soyad/tedarikçi PARÇA araması (canlıdaki Ad_Soyad kutusu): serbest metin
    /// <c>Tedarikci</c> alanında VE bağlı carinin ad/soyad/unvanında aranır — cari bağı olmayan
    /// eski kayıtlar da bulunabilsin diye iki yol birden taranır.
    /// </summary>
    public string? Ara { get; set; }

    /// <summary>
    /// Plaka/araç PARÇA araması (canlıdaki Plaka/Arac kutusu). <b>Not:</b> siparişteki araç henüz
    /// filoda YOKTUR (plaka teslimde doğar) → arama TSB/geçici plaka kaydı ile marka/tip/grup/
    /// versiyon metinlerinde yapılır. Bilinçli olarak <c>Vehicle</c> FK'si eklenmedi: sipariş
    /// kaydının canlıdaki karşılığında da araç FK'si yok.
    /// </summary>
    public string? Arac { get; set; }

    /// <summary>Dosya numarası parça eşleşmesi.</summary>
    public string? DosyaNo { get; set; }

    public OrderStatus? Durum { get; set; }

    /// <summary>Sipariş tarihi ≥ (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }

    /// <summary>Sipariş tarihi ≤ (dahil — çağıran gün sonunu geçirir).</summary>
    public DateTimeOffset? Bit { get; set; }

    /// <summary>Hiçbir alan dolu değilse filtre yok demektir (liste "Temizle" bağlantısı için).</summary>
    public bool Bos => CariId is null && string.IsNullOrWhiteSpace(Ara) && string.IsNullOrWhiteSpace(Arac)
        && string.IsNullOrWhiteSpace(DosyaNo) && Durum is null && Bas is null && Bit is null;
}
