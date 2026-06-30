namespace RentACar.Application.TenantSettings;

/// <summary>Ayarlar oku/yaz modeli (DÜZ METİN). Okumada sırlar çözülmüş; yazmada sır alanı dolu ise
/// güncellenir, BOŞ ise mevcut korunur ("değiştirmek için doldurun").</summary>
public sealed class TenantSettingsModel
{
    public string? FirmaUnvan { get; set; }
    public string? FirmaVergiDairesi { get; set; }
    public string? FirmaVergiNo { get; set; }
    public string? FirmaAdres { get; set; }
    public string? FirmaTel { get; set; }
    public string? FirmaEmail { get; set; }

    public string? EFaturaKullanici { get; set; }
    public string? EFaturaSifre { get; set; }
    public string? SmsBaslik { get; set; }
    public string? SmsApiKey { get; set; }
    public string? PosMerchantId { get; set; }
    public string? PosApiKey { get; set; }

    // Görünüm + operasyon kuralları + SMTP (roadmap M1)
    public string? LogoUrl { get; set; }
    public string? VarsayilanDoviz { get; set; }
    public decimal? VarsayilanKdvOrani { get; set; }
    public int? MinKiraGun { get; set; }
    public int? MaxKiraGun { get; set; }
    public bool? RezOnayZorunlu { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpKullanici { get; set; }
    public string? SmtpSifre { get; set; }
    public bool? SmtpSsl { get; set; }
}
