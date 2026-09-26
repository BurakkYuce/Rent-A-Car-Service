using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Legal;

/// <summary>
/// FAZ-41 — hukuk işlem listesi süzgeci (canlı <c>hukuk_islem_listesi.aspx</c>).
/// Tüm alanlar opsiyonel; hepsi boşsa liste filtresizdir.
/// </summary>
public sealed class HukukDosyaFilter
{
    /// <summary>Müşteri (cari) — canlıdaki "Ad Soyad" süzgecinin karşılığı; ekranda cari seçilir.</summary>
    public Guid? CariId { get; set; }
    /// <summary>Dosya tarihi aralığı — başlangıç (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }
    /// <summary>Dosya tarihi aralığı — bitiş (dahil; çağıran gün sonuna genişletir).</summary>
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Fatura no — içinde geçen (harf duyarsız).</summary>
    public string? FaturaNo { get; set; }
    /// <summary>Dosya no — içinde geçen (harf duyarsız).</summary>
    public string? DosyaNo { get; set; }
    /// <summary>Serbest metin: avukat adı / açıklama içinde geçen.</summary>
    public string? Ara { get; set; }
    public LegalType? Tur { get; set; }
    public LegalStatus? Durum { get; set; }
    public int EnFazla { get; set; } = 1000;
}

/// <summary>
/// FAZ-41 — liste satırı: dosya + ÇÖZÜLMÜŞ müşteri adı/telefonu. Ad snapshot DEĞİL, cariden her
/// istekte okunur (cari yeniden adlandırılırsa liste doğru kalır).
/// <para><see cref="HukukDosya.Kalan"/> ayrıca taşınmaz — entity'deki tek formülden okunur.</para>
/// </summary>
public sealed record HukukDosyaSatirDto(HukukDosya Dosya, string? MusteriAd, string? MusteriTel);
