using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç grubu tanımı + fiyat-kural master'ı: araçların gruplandığı liste (ör. "EKO"=Ekonomik) VE
/// gruba bağlı kira kuralları (SIPP/segment/kasa türü, koltuk/kapı/bagaj, sürücü yaşı/ehliyet yılı,
/// provizyon/muafiyet, günlük KM limiti + aşım ücreti). Canlı TürevRent "Araç Grubu Tanımı" karşılığı.
/// Tenant-owned + auditable. Kural alanları NULLABLE — yalnız Kod/Ad ile basit sözlük olarak da
/// kullanılabilir. Araç formundaki serbest-metin Grup alanını açılır listeden besler; Vehicle.Grup
/// kolonu STRING kalır (FK YOK — additive). Saf tanım tablosudur; deftere kayıt POSTLAMAZ.
/// </summary>
public class VehicleGroup : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    // Sınıflandırma
    /// <summary>SIPP/ACRISS kodu (ör. CDMD).</summary>
    public string? Sipp { get; set; }
    public string? Segment { get; set; }
    /// <summary>Kasa türü (ör. Sedan, Hatchback, SUV).</summary>
    public string? KasaTuru { get; set; }
    /// <summary>Temsili marka (canlı Araç Grubu "Marka" alanı).</summary>
    public string? Marka { get; set; }
    /// <summary>Temsili tip/model (canlı "Tipi").</summary>
    public string? Tipi { get; set; }
    public int? KoltukSayisi { get; set; }
    public int? KapiSayisi { get; set; }
    public int? BagajSayisi { get; set; }
    /// <summary>Küçük bagaj adedi (ACRISS).</summary>
    public int? KucukBagaj { get; set; }
    /// <summary>Büyük bagaj adedi (ACRISS).</summary>
    public int? BuyukBagaj { get; set; }

    // Kira kuralları
    public int? SurucuMinYas { get; set; }
    /// <summary>Genç sürücü eşik yaşı (altı = genç sürücü ek kuralı).</summary>
    public int? GencSurucuYas { get; set; }
    public int? EhliyetMinYil { get; set; }
    /// <summary>Genç sürücü için min ehliyet yılı (canlı "Genç Ehliyet Yılı").</summary>
    public int? GencEhliyetMinYil { get; set; }
    /// <summary>Provizyon (bloke) tutarı.</summary>
    public decimal? Provizyon { get; set; }
    /// <summary>İkinci provizyon tutarı (canlı Provizyon2).</summary>
    public decimal? Provizyon2 { get; set; }
    /// <summary>Hasar muafiyet tutarı.</summary>
    public decimal? MuafiyetTutari { get; set; }
    /// <summary>İkinci muafiyet tutarı (canlı Muafiyet2).</summary>
    public decimal? Muafiyet2 { get; set; }
    /// <summary>Günlük KM limiti (0/null = sınırsız).</summary>
    public int? GunlukKmLimiti { get; set; }
    /// <summary>Aylık max KM limiti.</summary>
    public int? AylikMaxKm { get; set; }
    /// <summary>Limit aşımı KM başına ücret.</summary>
    public decimal? AsimKmUcreti { get; set; }
    /// <summary>Grup için varsayılan yakıt fiyatı (eksik yakıt yansıtma referansı).</summary>
    public decimal? YakitFiyati { get; set; }
    /// <summary>"Sonra Öde" oranı (% — peşin olmayan rezervasyon için).</summary>
    public decimal? SonraOdeOran { get; set; }
    /// <summary>Kira için kredi kartı zorunlu mu.</summary>
    public bool? KrediKartiSart { get; set; }

    // Görünüm sıralaması
    /// <summary>Web sıralama (vitrin düzeni).</summary>
    public int? WebSira { get; set; }
    /// <summary>Upgrade sıralaması (yükseltme önceliği).</summary>
    public int? UpgradeSira { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
