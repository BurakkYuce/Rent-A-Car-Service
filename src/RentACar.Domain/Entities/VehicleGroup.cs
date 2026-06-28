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
    public int? KoltukSayisi { get; set; }
    public int? KapiSayisi { get; set; }
    public int? BagajSayisi { get; set; }

    // Kira kuralları
    public int? SurucuMinYas { get; set; }
    /// <summary>Genç sürücü eşik yaşı (altı = genç sürücü ek kuralı).</summary>
    public int? GencSurucuYas { get; set; }
    public int? EhliyetMinYil { get; set; }
    /// <summary>Provizyon (bloke) tutarı.</summary>
    public decimal? Provizyon { get; set; }
    /// <summary>Hasar muafiyet tutarı.</summary>
    public decimal? MuafiyetTutari { get; set; }
    /// <summary>Günlük KM limiti (0/null = sınırsız).</summary>
    public int? GunlukKmLimiti { get; set; }
    /// <summary>Limit aşımı KM başına ücret.</summary>
    public decimal? AsimKmUcreti { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
