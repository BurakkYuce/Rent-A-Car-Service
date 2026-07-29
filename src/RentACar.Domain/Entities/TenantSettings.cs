using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tenant ayarları (roadmap D1; canlı "ayarlar" karşılığı): firma bilgisi + entegrasyon kimlik slotları.
/// Tenant başına TEK satır (TenantId unique). Hassas alanlar (*Enc) at-rest ŞİFRELİ saklanır
/// (servis ISecretProtector ile yazar/okur); kullanıcı adı/başlık/merchant gibi gizli-olmayanlar düz metin.
/// Entegrasyonların (e-Fatura/SMS/POS) ön koşulu — değerler kimliksiz boş kurulur, sonra doldurulur.
/// </summary>
public class TenantSettings : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    // Firma (düz metin)
    public string? FirmaUnvan { get; set; }
    public string? FirmaVergiDairesi { get; set; }
    public string? FirmaVergiNo { get; set; }
    public string? FirmaAdres { get; set; }
    public string? FirmaTel { get; set; }
    public string? FirmaEmail { get; set; }
    public string? FirmaMobilTel { get; set; }   // 2. telefon (sözleşme başlığı MOBİL TEL)
    public string? FirmaMarka { get; set; }       // ticari marka / kısa ad (sözleşme sağ üst; yoksa Ünvan)

    // Entegrasyon kimlikleri — gizli-olmayan düz metin; sır (*Enc) ŞİFRELİ cipher.
    public string? EFaturaKullanici { get; set; }
    public string? EFaturaSifreEnc { get; set; }
    public string? SmsBaslik { get; set; }
    public string? SmsApiKeyEnc { get; set; }
    public string? PosMerchantId { get; set; }
    public string? PosApiKeyEnc { get; set; }

    // ---- Görünüm + operasyon kuralları + SMTP (roadmap M1; additive, nullable) ----
    public string? LogoUrl { get; set; }
    /// <summary>Firma logosu (PDF sözleşme/fatura/makbuz başlığında; PR-C). Ayarlar'dan yüklenir (PNG/JPG).</summary>
    public byte[]? LogoBytes { get; set; }
    /// <summary>Varsayılan döviz (3 harf).</summary>
    public string? VarsayilanDoviz { get; set; }
    /// <summary>Varsayılan KDV oranı (0..1).</summary>
    public decimal? VarsayilanKdvOrani { get; set; }
    public int? MinKiraGun { get; set; }
    public int? MaxKiraGun { get; set; }
    /// <summary>Rezervasyon onayı zorunlu mu (operasyon kuralı).</summary>
    public bool? RezOnayZorunlu { get; set; }
    // SMTP (host/port/kullanıcı düz metin; şifre *Enc şifreli)
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpKullanici { get; set; }
    public string? SmtpSifreEnc { get; set; }
    public bool? SmtpSsl { get; set; }

    // ---- WhatsApp günlük operasyon özeti (additive) ----
    /// <summary>Günlük özetin gideceği WhatsApp no (E.164; boşsa gönderilmez).</summary>
    public string? WhatsAppNumarasi { get; set; }
    /// <summary>Günlük operasyon özeti WhatsApp'tan gönderilsin mi.</summary>
    public bool WhatsAppGunlukOzet { get; set; }

    /// <summary>FAZ 4.2-B4: dönemsel faturalama job'ı bu tenant'ta çalışsın mı (default KAPALI —
    /// manuel-önce ilkesi; kira-başına ayrıca DonemselFaturalama bayrağı gerekir).</summary>
    public bool DonemselFaturalamaJob { get; set; }
    /// <summary>FAZ 4.2-B4: job kesilen dönem faturasına Kasa tahsilat kaydı da yazsın mı (default
    /// KAPALI — parasız tahsilat kaydı kasa gerçekliğini yalanlar; yalnız gerçek oto-ödeme akışında aç).</summary>
    public bool DonemselOtomatikTahsilat { get; set; }

    /// <summary>PR-0: tenant'ın halka açık pazarlama/rezervasyon sitesi (RentACar.PublicSite) aktif mi.
    /// "Sitemi Aç" ile true olur; kapatılan/pasif tenant'ta host çözümlenmiş olsa bile site 404 döner.</summary>
    public bool PublicSiteEnabled { get; set; }

    /// <summary>PR-10: grubu belirtilmeden açılan araçların düşeceği varsayılan araç grubu (FK →
    /// VehicleGroups, ON DELETE SET NULL). Boşsa çözücü Türkçe-duyarsız "Ekonomi" eşleşmesine, o da
    /// yoksa NULL'a düşer — "ilk aktif grup" gibi bir fallback BİLİNÇLİ OLARAK YOKTUR (o grup "Lüks"
    /// olabilir; yanlış segmentte yayınlanmaktansa araç grupsuz/pending kalır). Bkz. VarsayilanGrupCozucu.</summary>
    public Guid? VarsayilanGrupId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
