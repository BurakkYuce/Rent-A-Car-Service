using RentACar.Domain.Enums;

namespace RentACar.Application.AracKredileri;

/// <summary>
/// Araç kredisi liste filtresi (FAZ-13 — canlı `arac_kredi_listesi.aspx` paritesi). Boş alan = kısıt yok.
/// Tarih aralığı kredinin <c>BaslangicTarihi</c>'ne uygulanır (canlıdaki "Tarih" listesi alanı).
/// </summary>
public sealed class AracKrediFilter
{
    /// <summary>İlişkili cari (canlıdaki Musteri_No/Ad_Soyad araması).</summary>
    public Guid? CariId { get; set; }

    /// <summary>Plaka PARÇA eşleşmesi (canlıdaki Plaka/Arac araması). Boşluk/harf-durumu normalize edilir.</summary>
    public string? Plaka { get; set; }

    /// <summary>Dosya numarası parça eşleşmesi.</summary>
    public string? DosyaNo { get; set; }

    public LoanStatus? Durum { get; set; }

    /// <summary>Başlangıç tarihi ≥ (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }

    /// <summary>Başlangıç tarihi ≤ (dahil — çağıran gün sonunu geçirir).</summary>
    public DateTimeOffset? Bit { get; set; }

    /// <summary>Hiçbir alan dolu değilse filtre yok demektir (liste "Temizle" bağlantısı için).</summary>
    public bool Bos => CariId is null && string.IsNullOrWhiteSpace(Plaka) && string.IsNullOrWhiteSpace(DosyaNo)
        && Durum is null && Bas is null && Bit is null;
}
