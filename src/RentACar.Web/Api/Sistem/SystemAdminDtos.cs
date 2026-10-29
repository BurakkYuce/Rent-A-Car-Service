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
    public string? FirmaIl { get; init; }
    public string? FirmaIlce { get; init; }
    public string? EFaturaKullanici { get; init; }
    public bool EFaturaSifreTanimli { get; init; }
    /// <summary>Seçili entegratör adaptörünün anahtarı (null = seçilmedi).</summary>
    public string? EFaturaEntegrator { get; init; }
    public string? EFaturaGondericiEtiket { get; init; }
    public string? EFaturaPostaKutusuEtiket { get; init; }
    public bool EFaturaCanliOrtam { get; init; }
    public string? EFaturaSenaryo { get; init; }
    /// <summary>e-Belge bağlantısı hazır mı (ayar + kayıtlı adaptör + çözülebilen sır). Ağa çıkılmaz.</summary>
    public bool EFaturaBagli { get; init; }
    /// <summary>Bağlı değilse sebebi ("e-Fatura gönderilmedi: entegratör bağlı değil (…)").</summary>
    public string? EFaturaDurumMesaji { get; init; }
    /// <summary>Bu sürümde kayıtlı entegratör adaptörleri (boş = hiçbiri; kimlik bilgisi gelince eklenir).</summary>
    public IReadOnlyList<EInvoiceIntegratorOptionDto> EFaturaEntegratorSecenekleri { get; init; } = [];
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
    /// <summary>Halka açık site vurgu rengi ("#rrggbb" ya da null = sitenin varsayılanı).</summary>
    public string? SiteVurguRengi { get; init; }
    /// <summary>Halka açık site teması: <c>pazar</c> / <c>vitrin</c> / <c>rota</c> ya da null (= pazar).</summary>
    public string? SiteTemasi { get; init; }
    /// <summary>Halka açık site palet seti (seçili temanın setlerinden biri) ya da null (= temanın varsayılan seti).</summary>
    public string? SitePaleti { get; init; }
    /// <summary>Her temanın seçilebilir palet setleri (ilk = varsayılan) — seçim kartları için; salt okunur katalog.</summary>
    public IReadOnlyList<SiteThemePalettesDto> SitePaletSecenekleri { get; init; } = [];
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
    // Halka açık site bilgileri (V0) — boş alan sitede basılmaz.
    public decimal? SiteDepozitoTutari { get; init; }
    public string? SiteDepozitoAciklama { get; init; }
    public int? SiteGunlukKmSiniri { get; init; }
    public bool SiteKmSinirsiz { get; init; }
    public int? SiteMinYas { get; init; }
    public int? SiteMinEhliyetYili { get; init; }
    public string? SiteSigortaOzeti { get; init; }
    public string? SiteIptalKosulu { get; init; }
    public decimal? SiteGooglePuani { get; init; }
    public int? SiteGoogleYorumSayisi { get; init; }
    public string? SiteGoogleProfilUrl { get; init; }
    /// <summary>Puan/yorum sayısının en son girildiği an (sunucu yazar; salt okunur).</summary>
    public DateTimeOffset? SiteGooglePuanTarihi { get; init; }
    public int? SiteKurulusYili { get; init; }
    public string? Surum { get; init; }
}

/// <summary>Kayıtlı bir e-belge entegratör adaptörü (ayar ekranı seçeneği).</summary>
public sealed record EInvoiceIntegratorOptionDto(string Anahtar, string Ad);

/// <summary>Alan adı. Bekleyen özel alan adında DNS'e eklenecek TXT kaydının adı (<c>DogrulamaKaydi</c>) ve değeri.</summary>
public sealed record DomainDto(string Host, string Tur, string Durum, string? DogrulamaKaydi, string? DogrulamaDegeri);

public sealed record WhatsAppDeliveryDto(DateOnly Gun, string Tur, string Alici, bool Basarili, string? HataMesaji, DateTimeOffset OlusturmaUtc);

/// <summary>Bir temanın palet setleri (<c>SitePaletteKeys</c>); <c>Paletler[0]</c> varsayılan.</summary>
public sealed record SiteThemePalettesDto(string Tema, IReadOnlyList<SitePaletteOptionDto> Paletler);

/// <summary>Palet seti kartı: ad + renk çipleri ("#rrggbb"; <c>Vurgu</c> null = ana rengi izler).</summary>
public sealed record SitePaletteOptionDto(string Anahtar, string Ad, string Zemin, string Murekkep, string Ana,
    string? Vurgu, string GeceZemin);

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
    public string? FirmaIl { get; init; }
    public string? FirmaIlce { get; init; }
    public string? EFaturaKullanici { get; init; }
    /// <summary>e-Fatura entegratör şifresi ya da API anahtarı — YALNIZ YAZMA (boş = mevcut korunur).</summary>
    public string? EFaturaSifre { get; init; }
    /// <summary>Kayıtlı entegratör adaptörünün anahtarı ya da boş (= bağlı değil). Kayıtlı olmayan anahtar 400.</summary>
    public string? EFaturaEntegrator { get; init; }
    /// <summary>Gönderici birim etiketi (<c>urn:mail:…</c>) ya da boş.</summary>
    public string? EFaturaGondericiEtiket { get; init; }
    /// <summary>Posta kutusu etiketi (<c>urn:mail:…</c>) ya da boş.</summary>
    public string? EFaturaPostaKutusuEtiket { get; init; }
    /// <summary>true = canlı ortam; false = test.</summary>
    public bool EFaturaCanliOrtam { get; init; }
    /// <summary>TEMELFATURA ya da TICARIFATURA (boş = TEMELFATURA).</summary>
    public string? EFaturaSenaryo { get; init; }
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
    /// <summary>Halka açık site vurgu rengi: "#rrggbb" ya da boş (sitenin varsayılanı). Tam değiştirme — boş gelirse silinir.</summary>
    public string? SiteVurguRengi { get; init; }
    /// <summary>Halka açık site teması: <c>pazar</c> / <c>vitrin</c> / <c>rota</c> ya da boş (= pazar). Tam değiştirme.
    /// Eski anahtar (<c>tarife</c>, <c>kontuar</c>) kabul edilir ve yeni karşılığı saklanır (<c>SiteThemeKeys.Normalize</c>).</summary>
    public string? SiteTemasi { get; init; }
    /// <summary>Halka açık site palet seti: AYNI istekteki temanın (<c>siteTemasi</c>; boş = tarife) setlerinden biri ya
    /// da boş (= temanın varsayılan seti). Biçimsiz ya da temaya ait olmayan anahtar 400 <c>errors.sitePaleti</c>.</summary>
    public string? SitePaleti { get; init; }
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
    /// <summary>Depozito (TL, 0–10.000.000, en çok 2 ondalık) ya da boş.</summary>
    public decimal? SiteDepozitoTutari { get; init; }
    public string? SiteDepozitoAciklama { get; init; }
    /// <summary>Günlük km sınırı (1–10.000); <c>siteKmSinirsiz</c> ile birlikte dolu olamaz.</summary>
    public int? SiteGunlukKmSiniri { get; init; }
    public bool SiteKmSinirsiz { get; init; }
    /// <summary>Asgari yaş (18–99).</summary>
    public int? SiteMinYas { get; init; }
    /// <summary>Asgari ehliyet yılı (0–60).</summary>
    public int? SiteMinEhliyetYili { get; init; }
    public string? SiteSigortaOzeti { get; init; }
    public string? SiteIptalKosulu { get; init; }
    /// <summary>Google puanı (1,0–5,0, bir ondalık); doluysa <c>siteGoogleProfilUrl</c> zorunlu.</summary>
    public decimal? SiteGooglePuani { get; init; }
    /// <summary>Google yorum sayısı; doluysa puan zorunlu.</summary>
    public int? SiteGoogleYorumSayisi { get; init; }
    /// <summary>Google işletme profili: yalnız https ve Google alan adları (google.com[.tr], g.page, maps.app.goo.gl …).</summary>
    public string? SiteGoogleProfilUrl { get; init; }
    /// <summary>Kuruluş yılı (1900–bu yıl).</summary>
    public int? SiteKurulusYili { get; init; }
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
