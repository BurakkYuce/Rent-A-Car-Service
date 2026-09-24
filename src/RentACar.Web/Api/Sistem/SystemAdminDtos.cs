namespace RentACar.Web.Api.Sistem;

/// <summary>
/// Firma ayarları (GET). SIR alanları (e-Fatura şifresi, SMS API anahtarı, POS API anahtarı, SMTP şifresi) HİÇBİR
/// yanıtta dönmez — yalnız <c>*Tanimli</c> bayrağı. Logo baytları ayrı uçtan (<c>/ayarlar/logo</c>) okunur.
/// <c>YeniArayuzPilot</c> salt okunur (platform konsolu yazar).
/// </summary>
public sealed class SettingsDto
{
    public string? FirmaUnvan { get; init; }
    public string? FirmaVergiDairesi { get; init; }
    public string? FirmaVergiNo { get; init; }
    public string? FirmaAdres { get; init; }
    public string? FirmaTel { get; init; }
    public string? FirmaEmail { get; init; }
    public string? FirmaMobilTel { get; init; }
    public string? FirmaMarka { get; init; }
    public string? EFaturaKullanici { get; init; }
    public bool EFaturaSifreTanimli { get; init; }
    public string? SmsBaslik { get; init; }
    public bool SmsApiKeyTanimli { get; init; }
    public string? PosMerchantId { get; init; }
    public bool PosApiKeyTanimli { get; init; }
    public string? LogoUrl { get; init; }
    public bool LogoVar { get; init; }
    public string? VarsayilanDoviz { get; init; }
    public decimal? VarsayilanKdvOrani { get; init; }
    public Guid? VarsayilanGrupId { get; init; }
    public string? VarsayilanFiyatTuru { get; init; }
    public int? VarsayilanYakitSeviyesi { get; init; }
    public bool? DropMesafeYokIseSifir { get; init; }
    public int? SaatFarkiToleransDk { get; init; }
    public int? IadeIslemSaatSiniri { get; init; }
    public bool KurElleGirisKilitli { get; init; }
    public string? RenkGecikenler { get; init; }
    public string? RenkBugunDonecekler { get; init; }
    public string? RenkBugunCikacaklar { get; init; }
    public string? RenkOpsiyonlu { get; init; }
    public string? RenkLimitBakiye { get; init; }
    public string? RenkAlacakli { get; init; }
    public string? RenkRezAtananPlaka { get; init; }
    public string? RenkKiralanmayan { get; init; }
    public bool DonemselFaturalamaJob { get; init; }
    public bool DonemselOtomatikTahsilat { get; init; }
    public int? MinKiraGun { get; init; }
    public int? MaxKiraGun { get; init; }
    public bool? RezOnayZorunlu { get; init; }
    public string? SmtpHost { get; init; }
    public int? SmtpPort { get; init; }
    public string? SmtpKullanici { get; init; }
    public bool SmtpSifreTanimli { get; init; }
    public bool? SmtpSsl { get; init; }
    public string? SmtpGonderenAdres { get; init; }
    public string? SmtpGonderenAd { get; init; }
    public string? FaturaSeriKodu { get; init; }
    public string? WhatsAppNumarasi { get; init; }
    public bool? WhatsAppGunlukOzet { get; init; }
    public bool WebSitesiAcik { get; init; }
    public string? WebSitesiAdresi { get; init; }
    public IReadOnlyList<DomainDto> Domainler { get; init; } = [];
    public bool YeniArayuzPilot { get; init; }
    /// <summary>Sunucu Twilio yapılandırması var mı (SMS/WhatsApp test düğmeleri için bilgi; kimlik değeri dönmez).</summary>
    public bool TwilioTanimli { get; init; }
    public IReadOnlyList<WhatsAppDeliveryDto> WhatsAppGonderimleri { get; init; } = [];
    public string? Surum { get; init; }
}

/// <summary>Alan adı. Bekleyen özel alan adında DNS'e eklenecek TXT kaydının adı (<c>DogrulamaKaydi</c>) ve değeri.</summary>
public sealed record DomainDto(string Host, string Tur, string Durum, string? DogrulamaKaydi, string? DogrulamaDegeri);

public sealed record WhatsAppDeliveryDto(DateOnly Gun, string Tur, string Alici, bool Basarili, string? HataMesaji, DateTimeOffset OlusturmaUtc);

/// <summary>
/// Firma ayarları PUT gövdesi (tam değiştirme). SIR alanları YALNIZ YAZMA içindir: boş/null gelirse mevcut değer
/// KORUNUR. Ayar satırı varsa <c>surum</c> ZORUNLU; uyuşmazlık 409 <c>cakisma</c>.
/// </summary>
public sealed class SettingsRequest
{
    public string? FirmaUnvan { get; init; }
    public string? FirmaVergiDairesi { get; init; }
    public string? FirmaVergiNo { get; init; }
    public string? FirmaAdres { get; init; }
    public string? FirmaTel { get; init; }
    public string? FirmaEmail { get; init; }
    public string? FirmaMobilTel { get; init; }
    public string? FirmaMarka { get; init; }
    public string? EFaturaKullanici { get; init; }
    public string? EFaturaSifre { get; init; }
    public string? SmsBaslik { get; init; }
    public string? SmsApiKey { get; init; }
    public string? PosMerchantId { get; init; }
    public string? PosApiKey { get; init; }
    /// <summary>true: kayıtlı e-Fatura şifresi silinir (aynı istekte <c>eFaturaSifre</c> doluysa o yazılır).</summary>
    public bool EFaturaSifreTemizle { get; init; }
    /// <summary>true: kayıtlı SMS API anahtarı silinir (aynı istekte <c>smsApiKey</c> doluysa o yazılır).</summary>
    public bool SmsApiKeyTemizle { get; init; }
    /// <summary>true: kayıtlı POS API anahtarı silinir (aynı istekte <c>posApiKey</c> doluysa o yazılır).</summary>
    public bool PosApiKeyTemizle { get; init; }
    /// <summary>true: kayıtlı SMTP şifresi silinir (aynı istekte <c>smtpSifre</c> doluysa o yazılır).</summary>
    public bool SmtpSifreTemizle { get; init; }
    public string? LogoUrl { get; init; }
    public string? VarsayilanDoviz { get; init; }
    public decimal? VarsayilanKdvOrani { get; init; }
    public Guid? VarsayilanGrupId { get; init; }
    public string? VarsayilanFiyatTuru { get; init; }
    public int? VarsayilanYakitSeviyesi { get; init; }
    public bool? DropMesafeYokIseSifir { get; init; }
    public int? SaatFarkiToleransDk { get; init; }
    public int? IadeIslemSaatSiniri { get; init; }
    public bool KurElleGirisKilitli { get; init; }
    public string? RenkGecikenler { get; init; }
    public string? RenkBugunDonecekler { get; init; }
    public string? RenkBugunCikacaklar { get; init; }
    public string? RenkOpsiyonlu { get; init; }
    public string? RenkLimitBakiye { get; init; }
    public string? RenkAlacakli { get; init; }
    public string? RenkRezAtananPlaka { get; init; }
    public string? RenkKiralanmayan { get; init; }
    public bool DonemselFaturalamaJob { get; init; }
    public bool DonemselOtomatikTahsilat { get; init; }
    public int? MinKiraGun { get; init; }
    public int? MaxKiraGun { get; init; }
    public bool? RezOnayZorunlu { get; init; }
    public string? SmtpHost { get; init; }
    public int? SmtpPort { get; init; }
    public string? SmtpKullanici { get; init; }
    public string? SmtpSifre { get; init; }
    public bool? SmtpSsl { get; init; }
    public string? SmtpGonderenAdres { get; init; }
    public string? SmtpGonderenAd { get; init; }
    public string? FaturaSeriKodu { get; init; }
    public string? WhatsAppNumarasi { get; init; }
    public bool? WhatsAppGunlukOzet { get; init; }
    public string? Surum { get; init; }
}

public sealed record DomainAddRequest(string? Host);

public sealed record EmailTestRequest(string? Alici);

public sealed record PhoneTestRequest(string? Telefon);

/// <summary>
/// Test gönderimi sonucu — dürüst: yapılandırma yoksa ya da teslim doğrulanamadıysa <c>Basarili=false</c>.
/// <c>Durum</c>: <c>gonderildi</c> (SMTP kabul etti / teslim doğrulandı), <c>belirsiz</c> (iletildi, teslim henüz
/// bilinmiyor), <c>teslim_edilemedi</c>, <c>yapilandirma_yok</c>, <c>basarisiz</c>.
/// </summary>
public sealed record SendTestResult(bool Basarili, string Durum, string Mesaj);

public sealed record LogoDto(bool LogoVar, int? Genislik, int? Yukseklik, int? Bayt);
