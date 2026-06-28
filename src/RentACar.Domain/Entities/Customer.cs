using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Cari (müşteri). Bireysel / Kurumsal / Servis. PR #2 çekirdeği — orijinal
/// Musteri_Kayit.Aspx'in tüm alanları değil. Tenant-owned + auditable: EF filter +
/// RLS ile izole, değişiklikleri AuditLog'a yazılır.
/// Benzersizlik (tenant içinde): bireysel için TC Kimlik, kurumsal için Vergi No
/// (her ikisi de yalnız doluyken — kısmi unique index).
/// </summary>
public class Customer : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public CariType Tip { get; set; } = CariType.Bireysel;

    // Bireysel
    public string? Ad { get; set; }
    public string? Soyad { get; set; }
    public string? TcKimlik { get; set; }

    // Kurumsal / Servis
    public string? Unvan { get; set; }
    public string? VergiDairesi { get; set; }
    public string? VergiNo { get; set; }

    // İletişim / adres
    public string? CepTel { get; set; }
    public string? Gsm2 { get; set; }
    public string? Email { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Adres { get; set; }

    // CRM
    /// <summary>Müşteri kaynağı (ör. Web, Telefon, Bayi, Tavsiye).</summary>
    public string? Kaynak { get; set; }
    public string? MusteriTemsilcisi { get; set; }
    /// <summary>İYS (ileti yönetim sistemi) izinli mi?</summary>
    public bool IysIzinli { get; set; }
    /// <summary>Operasyonel uyarı bayrağı (kara listeden ayrı, hafif uyarı).</summary>
    public bool Uyari { get; set; }
    public string? UyariNedeni { get; set; }

    // ---- CRM parite zenginleştirme (docs/parite/03; additive, NULLABLE) ----
    /// <summary>Müşteri sınıfı/segment (canlı Mst_Sinif: Düşük/Orta/Yüksek/VIP/Personel/Problemli).</summary>
    public string? Sinif { get; set; }
    /// <summary>Kanal-bazlı izin: e-posta (İYS'den ayrı, kanal granülerliği).</summary>
    public bool? MailIzin { get; set; }
    /// <summary>Kanal-bazlı izin: SMS.</summary>
    public bool? SmsIzin { get; set; }
    /// <summary>Kanal-bazlı izin: telefon arama.</summary>
    public bool? TelefonIzin { get; set; }
    public DateTimeOffset? DogumTarihi { get; set; }
    public string? BabaAdi { get; set; }
    public string? AnaAdi { get; set; }
    public string? PasaportNo { get; set; }
    /// <summary>Fatura dönemi (ör. Aylık, 15 günlük, Peşin).</summary>
    public string? FaturaDonemi { get; set; }
    /// <summary>Tevkifat oranı (% — kurumsal stopaj).</summary>
    public decimal? TevkifatOrani { get; set; }

    // Kurumsal yetkili kişiler (×3)
    public string? Yetkili1Ad { get; set; }
    public string? Yetkili1Tel { get; set; }
    public string? Yetkili1Mail { get; set; }
    public string? Yetkili2Ad { get; set; }
    public string? Yetkili2Tel { get; set; }
    public string? Yetkili2Mail { get; set; }
    public string? Yetkili3Ad { get; set; }
    public string? Yetkili3Tel { get; set; }
    public string? Yetkili3Mail { get; set; }

    // Ehliyet
    public string? EhliyetNo { get; set; }
    public string? EhliyetSinifi { get; set; }
    public DateTimeOffset? EhliyetTarihi { get; set; }
    public string? EhliyetYeri { get; set; }

    // Finans / risk
    public string? Tarife { get; set; }
    public int VadeGun { get; set; }
    public decimal RiskLimiti { get; set; }
    public string? RiskMesaji { get; set; }
    public DateTimeOffset? RiskTarihi { get; set; }
    /// <summary>HGS/geçiş yansıtma türü (ör. Faturalı, Faturasız, Yansıtılmaz).</summary>
    public string? HgsYansitmaTuru { get; set; }
    public bool KaraListe { get; set; }
    public bool Pasif { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>Görünen ad: kurumsal → Ünvan; bireysel → "Ad Soyad". (Mapped değil.)</summary>
    public string DisplayName => Tip == CariType.Bireysel
        ? $"{Ad} {Soyad}".Trim()
        : (Unvan ?? string.Empty);
}
