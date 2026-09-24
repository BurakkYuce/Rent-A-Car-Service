using System.Net;
using System.Text.RegularExpressions;
using RentACar.Application.Common;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Sistem;

public static partial class SystemAdminApi
{
    /// <summary>Platformun kendi alt alan adı uzayı (TenantDomainRepository.EnsureSubdomainAsync ile aynı).</summary>
    private const string PlatformBaseDomain = "rentpro.com";

    /// <summary>Servis mesajı → alan (önek eşleşmesi).</summary>
    private static readonly (string, string)[] SettingsRules =
    [
        ("Varsayılan KDV oranı", "varsayilanKdvOrani"), ("Varsayılan fiyat türü", "varsayilanFiyatTuru"),
        ("Varsayılan yakıt seviyesi", "varsayilanYakitSeviyesi"), ("Saat farkı toleransı", "saatFarkiToleransDk"),
        ("İade işlem saat sınırı", "iadeIslemSaatSiniri"), ("Fatura seri kodu", "faturaSeriKodu"),
        ("RenkGecikenler", "renkGecikenler"), ("RenkBugunDonecekler", "renkBugunDonecekler"),
        ("RenkBugunCikacaklar", "renkBugunCikacaklar"), ("RenkOpsiyonlu", "renkOpsiyonlu"),
        ("RenkLimitBakiye", "renkLimitBakiye"), ("RenkAlacakli", "renkAlacakli"),
        ("RenkRezAtananPlaka", "renkRezAtananPlaka"), ("RenkKiralanmayan", "renkKiralanmayan"),
        ("SMTP şifresi", "smtpSifre"), ("e-Fatura şifresi", "eFaturaSifre"), ("POS API anahtarı", "posApiKey"),
    ];

    private static readonly (string, string)[] LogoRules = [("Logo", "dosya")];

    private static readonly (string, string)[] DomainRules = [("Alan adı", "host"), ("Aynı anda en fazla", "host"), ("'", "host")];

    /// <summary>
    /// Uç sınırları: kolon uzunlukları (SettingsConfigs), port/gün aralıkları ve Logo URL şeması. İş kuralları
    /// (KDV 0–1, renk biçimi, fiyat türü listesi, seri kodu biçimi) serviste.
    /// </summary>
    private static void ValidateSettings(SettingsRequest i)
    {
        Sinirlar.Metin(i.FirmaUnvan, 256, "firmaUnvan", "Firma ünvanı");
        Sinirlar.Metin(i.FirmaVergiDairesi, 128, "firmaVergiDairesi", "Vergi dairesi");
        Sinirlar.Metin(i.FirmaVergiNo, 32, "firmaVergiNo", "Vergi no");
        Sinirlar.Metin(i.FirmaAdres, 512, "firmaAdres", "Adres");
        Sinirlar.Metin(i.FirmaTel, 64, "firmaTel", "Telefon");
        Sinirlar.Metin(i.FirmaEmail, 128, "firmaEmail", "E-posta");
        Sinirlar.Metin(i.FirmaMobilTel, 64, "firmaMobilTel", "Mobil telefon");
        Sinirlar.Metin(i.FirmaMarka, 128, "firmaMarka", "Marka");
        Sinirlar.Metin(i.EFaturaKullanici, 128, "eFaturaKullanici", "e-Fatura kullanıcısı");
        Sinirlar.Metin(i.EFaturaSifre, SecretMaxLength, "eFaturaSifre", "e-Fatura şifresi");
        Sinirlar.Metin(i.SmsBaslik, 64, "smsBaslik", "SMS başlığı");
        Sinirlar.Metin(i.SmsApiKey, SecretMaxLength, "smsApiKey", "SMS API anahtarı");
        Sinirlar.Metin(i.PosMerchantId, 128, "posMerchantId", "POS üye işyeri no");
        Sinirlar.Metin(i.PosApiKey, SecretMaxLength, "posApiKey", "POS API anahtarı");
        Sinirlar.Metin(i.LogoUrl, 512, "logoUrl", "Logo URL");
        Sinirlar.Metin(i.VarsayilanDoviz, 3, "varsayilanDoviz", "Varsayılan döviz");
        Sinirlar.Metin(i.VarsayilanFiyatTuru, 32, "varsayilanFiyatTuru", "Varsayılan fiyat türü");
        Sinirlar.Metin(i.SmtpHost, 256, "smtpHost", "SMTP sunucusu");
        Sinirlar.Metin(i.SmtpKullanici, 256, "smtpKullanici", "SMTP kullanıcısı");
        Sinirlar.Metin(i.SmtpSifre, SecretMaxLength, "smtpSifre", "SMTP şifresi");
        Sinirlar.Metin(i.SmtpGonderenAdres, 256, "smtpGonderenAdres", "Gönderen adresi");
        Sinirlar.Metin(i.SmtpGonderenAd, 128, "smtpGonderenAd", "Gönderen adı");
        Sinirlar.Metin(i.FaturaSeriKodu, 3, "faturaSeriKodu", "Fatura seri kodu");
        Sinirlar.Metin(i.WhatsAppNumarasi, 32, "whatsAppNumarasi", "WhatsApp numarası");
        foreach (var (value, field) in new[]
                 {
                     (i.RenkGecikenler, "renkGecikenler"), (i.RenkBugunDonecekler, "renkBugunDonecekler"),
                     (i.RenkBugunCikacaklar, "renkBugunCikacaklar"), (i.RenkOpsiyonlu, "renkOpsiyonlu"),
                     (i.RenkLimitBakiye, "renkLimitBakiye"), (i.RenkAlacakli, "renkAlacakli"),
                     (i.RenkRezAtananPlaka, "renkRezAtananPlaka"), (i.RenkKiralanmayan, "renkKiralanmayan"),
                 })
            Sinirlar.Metin(value, 7, field, "Renk kodu");

        // F11.1b güvenlik M4: yalnız SMTP portları (gönderici de aynı listeyi uygular).
        if (i.SmtpPort is { } port && !Infrastructure.Integrations.SmtpEndpointGuard.AllowedPorts.Contains(port))
            throw new ValidationException("SMTP portu yalnız 25, 465, 587 ya da 2525 olabilir.", "smtpPort");
        if (i.MinKiraGun is < 0 or > 3650)
            throw new ValidationException("En az kira günü 0 ile 3650 arasında olmalıdır.", "minKiraGun");
        if (i.MaxKiraGun is < 0 or > 3650)
            throw new ValidationException("En çok kira günü 0 ile 3650 arasında olmalıdır.", "maxKiraGun");
        if (i.MinKiraGun is { } min && i.MaxKiraGun is { } max && max > 0 && min > max)
            throw new ValidationException("En az kira günü en çok kira gününden büyük olamaz.", "minKiraGun");
        if (i.SaatFarkiToleransDk is > 1440)
            throw new ValidationException("Saat farkı toleransı en fazla 1440 dakika olabilir.", "saatFarkiToleransDk");
        if (i.IadeIslemSaatSiniri is > 8760)
            throw new ValidationException("İade işlem saat sınırı en fazla 8760 saat olabilir.", "iadeIslemSaatSiniri");

        // Logo URL ekranda <img src>/bağlantı olarak basılır: yalnız mutlak http(s) — javascript:/data: gibi şemalar
        // (XSS) ve göreli yollar reddedilir.
        if (SystemApiCommon.Clean(i.LogoUrl) is { } url
            && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)))
            throw new ValidationException("Logo URL http:// ya da https:// ile başlayan geçerli bir adres olmalıdır.", "logoUrl");
    }

    private static readonly Regex HostLabel = new("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Özel alan adı biçimi — Blazor ucunda YOK; yeni yüzey sertleştirildi: yalnız DNS adı (IP, port, yol, joker
    /// yok), en az iki etiket, harfli TLD. Platformun kendi alt alan adı uzayı (<c>*.rentpro.com</c>) reddedilir —
    /// aksi halde bir firma, henüz sitesini açmamış başka bir firmanın alt alan adını önceden sahiplenebilirdi.
    /// </summary>
    internal static string NormalizeCustomHost(string? host)
    {
        var h = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (h.Length == 0) throw new ValidationException("Alan adı zorunludur.", "host");
        if (h.Length > 253 || IPAddress.TryParse(h, out _))
            throw new ValidationException("Alan adı geçerli bir DNS adı olmalıdır (ör. www.firmam.com).", "host");
        var labels = h.Split('.');
        if (labels.Length < 2 || !labels.All(HostLabel.IsMatch) || !labels[^1].All(char.IsAsciiLetter))
            throw new ValidationException("Alan adı geçerli bir DNS adı olmalıdır (ör. www.firmam.com).", "host");
        if (h == PlatformBaseDomain || h.EndsWith("." + PlatformBaseDomain, StringComparison.Ordinal))
            throw new ValidationException("Platform alan adı özel alan adı olarak eklenemez.", "host");
        return h;
    }
}
