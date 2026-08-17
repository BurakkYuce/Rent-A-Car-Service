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
    public string? FirmaMobilTel { get; set; }
    public string? FirmaMarka { get; set; }

    public string? EFaturaKullanici { get; set; }
    public string? EFaturaSifre { get; set; }
    public string? SmsBaslik { get; set; }
    public string? SmsApiKey { get; set; }
    public string? PosMerchantId { get; set; }
    public string? PosApiKey { get; set; }

    // Görünüm + operasyon kuralları + SMTP (roadmap M1)
    public string? LogoUrl { get; set; }
    public byte[]? LogoBytes { get; set; } // PR-C: PDF başlığı için firma logosu (Ayarlar'dan yüklenir)
    public string? VarsayilanDoviz { get; set; }
    public decimal? VarsayilanKdvOrani { get; set; }
    /// <summary>PR-10: grubu belirtilmeden açılan araçların düşeceği grup (boş → "Ekonomi" eşleşmesi → grupsuz).</summary>
    public Guid? VarsayilanGrupId { get; set; }
    public bool DonemselFaturalamaJob { get; set; }   // FAZ 4.2-B4
    public bool DonemselOtomatikTahsilat { get; set; } // FAZ 4.2-B4
    public int? MinKiraGun { get; set; }
    public int? MaxKiraGun { get; set; }
    public bool? RezOnayZorunlu { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpKullanici { get; set; }
    public string? SmtpSifre { get; set; }
    public bool? SmtpSsl { get; set; }
    public string? SmtpGonderenAdres { get; set; }
    public string? SmtpGonderenAd { get; set; }

    // WhatsApp günlük operasyon özeti
    public string? WhatsAppNumarasi { get; set; }
    public bool? WhatsAppGunlukOzet { get; set; }

    // FAZ-82 fiyat/muhasebe varsayılanları + iş kuralı anahtarı (hepsi opsiyonel; null = bugünkü davranış)
    public string? VarsayilanFiyatTuru { get; set; }
    public int? VarsayilanYakitSeviyesi { get; set; }
    public bool? DropMesafeYokIseSifir { get; set; }   // BEKLEMEDE — motora bağlı değil
    public int? SaatFarkiToleransDk { get; set; }      // BEKLEMEDE — motora bağlı değil
    public int? IadeIslemSaatSiniri { get; set; }      // BEKLEMEDE — motora bağlı değil
    public bool KurElleGirisKilitli { get; set; }      // UYGULANIYOR (KurCozucu)

    // FAZ-81 görünüm renk kodları ("#rrggbb" ya da null)
    public string? RenkGecikenler { get; set; }
    public string? RenkBugunDonecekler { get; set; }
    public string? RenkBugunCikacaklar { get; set; }
    public string? RenkOpsiyonlu { get; set; }
    public string? RenkLimitBakiye { get; set; }
    public string? RenkAlacakli { get; set; }
    public string? RenkRezAtananPlaka { get; set; }
    public string? RenkKiralanmayan { get; set; }

    // PR-2: public-site — yalnız GÖRÜNTÜLEME (SaveAsync bunları yazmaz; OpenPublicSiteAsync yazar).
    public bool PublicSiteEnabled { get; set; }
    public string? PublicSiteHost { get; set; }

    /// <summary>PR-5: tenant'ın TÜM domain kayıtları (subdomain + özel) — Ayarlar ekranında durum
    /// rozetiyle (Aktif/Doğrulama Bekliyor/Başarısız) listelenir. Yalnız GÖRÜNTÜLEME.</summary>
    public IReadOnlyList<TenantDomainRow> CustomDomains { get; set; } = [];
}

public sealed record TenantDomainRow(string Host, string Kind, string Durum);
