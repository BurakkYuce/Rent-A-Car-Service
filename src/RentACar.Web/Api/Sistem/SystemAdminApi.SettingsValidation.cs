using RentACar.Application.Common;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Sistem;

public static partial class SystemAdminApi
{
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
        RentalLimits.Text(i.FirmaUnvan, 256, "firmaUnvan", "Firma ünvanı");
        RentalLimits.Text(i.FirmaVergiDairesi, 128, "firmaVergiDairesi", "Vergi dairesi");
        RentalLimits.Text(i.FirmaVergiNo, 32, "firmaVergiNo", "Vergi no");
        RentalLimits.Text(i.FirmaAdres, 512, "firmaAdres", "Adres");
        RentalLimits.Text(i.FirmaTel, 64, "firmaTel", "Telefon");
        RentalLimits.Text(i.FirmaEmail, 128, "firmaEmail", "E-posta");
        RentalLimits.Text(i.FirmaMobilTel, 64, "firmaMobilTel", "Mobil telefon");
        RentalLimits.Text(i.FirmaMarka, 128, "firmaMarka", "Marka");
        RentalLimits.Text(i.EFaturaKullanici, 128, "eFaturaKullanici", "e-Fatura kullanıcısı");
        RentalLimits.Text(i.EFaturaSifre, SecretMaxLength, "eFaturaSifre", "e-Fatura şifresi");
        RentalLimits.Text(i.SmsBaslik, 64, "smsBaslik", "SMS başlığı");
        RentalLimits.Text(i.SmsApiKey, SecretMaxLength, "smsApiKey", "SMS API anahtarı");
        RentalLimits.Text(i.PosMerchantId, 128, "posMerchantId", "POS üye işyeri no");
        RentalLimits.Text(i.PosApiKey, SecretMaxLength, "posApiKey", "POS API anahtarı");
        RentalLimits.Text(i.LogoUrl, 512, "logoUrl", "Logo URL");
        RentalLimits.Text(i.VarsayilanDoviz, 3, "varsayilanDoviz", "Varsayılan döviz");
        RentalLimits.Text(i.VarsayilanFiyatTuru, 32, "varsayilanFiyatTuru", "Varsayılan fiyat türü");
        RentalLimits.Text(i.SmtpHost, 256, "smtpHost", "SMTP sunucusu");
        RentalLimits.Text(i.SmtpKullanici, 256, "smtpKullanici", "SMTP kullanıcısı");
        RentalLimits.Text(i.SmtpSifre, SecretMaxLength, "smtpSifre", "SMTP şifresi");
        RentalLimits.Text(i.SmtpGonderenAdres, 256, "smtpGonderenAdres", "Gönderen adresi");
        RentalLimits.Text(i.SmtpGonderenAd, 128, "smtpGonderenAd", "Gönderen adı");
        RentalLimits.Text(i.FaturaSeriKodu, 3, "faturaSeriKodu", "Fatura seri kodu");
        RentalLimits.Text(i.WhatsAppNumarasi, 32, "whatsAppNumarasi", "WhatsApp numarası");
        foreach (var (value, field) in new[]
                 {
                     (i.RenkGecikenler, "renkGecikenler"), (i.RenkBugunDonecekler, "renkBugunDonecekler"),
                     (i.RenkBugunCikacaklar, "renkBugunCikacaklar"), (i.RenkOpsiyonlu, "renkOpsiyonlu"),
                     (i.RenkLimitBakiye, "renkLimitBakiye"), (i.RenkAlacakli, "renkAlacakli"),
                     (i.RenkRezAtananPlaka, "renkRezAtananPlaka"), (i.RenkKiralanmayan, "renkKiralanmayan"),
                 })
            RentalLimits.Text(value, 7, field, "Renk kodu");

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

    /// <summary>Özel alan adı biçimi — kural serviste (<see cref="Application.TenantSettings.DomainVerification.NormalizeCustomHost"/>;
    /// Blazor ve API tek yol). Uç yalnız erken ve alan-eşlemeli hata için çağırır.</summary>
    internal static string NormalizeCustomHost(string? host)
        => Application.TenantSettings.DomainVerification.NormalizeCustomHost(host);
}
