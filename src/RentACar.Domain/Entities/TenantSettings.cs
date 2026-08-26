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
    /// <summary>Giden e-postaların "Kimden" adresi. Ayrı alandır çünkü kimlik doğrulama kullanıcı adı
    /// çoğu sağlayıcıda e-posta DEĞİLDİR ve alan adı doğrulaması (SPF/DKIM) gönderen adrese bakar.
    /// Boşsa kullanıcı adı e-posta biçimindeyse ona düşülür.</summary>
    public string? SmtpGonderenAdres { get; set; }
    /// <summary>Giden e-postalarda görünecek gönderen adı (boşsa firma unvanı kullanılır).</summary>
    public string? SmtpGonderenAd { get; set; }

    // ---- Fatura numaralandırma (GİB) ----
    /// <summary>
    /// e-Fatura/e-Arşiv fatura numarasının 3 karakterlik SERİ kodu (ör. "RNT"). Mevzuat: numara
    /// 16 hane = seri(3) + yıl(4) + sıra(9), sıra her yıl 1'den başlar. Seri yalnız büyük harf
    /// (A-Z) veya rakam olabilir; Türkçe karakter kabul edilmez.
    ///
    /// <para>Boşsa fatura numarası ÜRETİLMEZ (gürültülü red) — sessiz bir varsayılan uydurmak,
    /// değiştirilemez bir mali kayda yanlış seri yazardı.</para>
    /// </summary>
    public string? FaturaSeriKodu { get; set; }

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

    // ---- FAZ-82: fiyat/muhasebe varsayılanları + iş kuralı anahtarı (canlı ayarlar.aspx) ----
    // GENEL POLİTİKA (docs/roadmap/KARARLAR.md): buradaki hiçbir alan DEFTERE YAZMAZ; en fazla bir
    // FORMUN ön-seçili değerini belirler ya da bir girişi REDDEDER. Hepsi nullable/false-varsayılan →
    // ayar boşken davranış bugünküyle BAYT-ÖZDEŞ kalır (regresyon testleri: AyarFiyatKuralTests).

    /// <summary>
    /// Yeni rezervasyon/teklif/kira formlarında "Fiyat Türü" seçiminin ÖN-SEÇİLİ değeri
    /// (null = bugünkü davranış: rezervasyon/kirada boş "—", teklifte listenin ilki).
    ///
    /// <para>UYGULANIYOR mu: yalnızca FORM ÖN-DOLDURMA olarak. Sunucu tarafında hiçbir servis bu
    /// değeri okuyup kaydın <c>FiyatTuru</c>'sunu türetmez — kullanıcı seçimi tek kaynaktır.
    /// GEREKÇE: <c>FiyatTuru</c> masum bir etiket DEĞİLDİR; "Otomatik" manuel ücreti yok saydırır
    /// (<c>PricingService</c>), "Günlük"/"Toplam" ise faturayı NET moda çevirir
    /// (<c>InvoiceService</c> — KDV tutarın üstüne eklenir). Bu yüzden değer yalnız operatörün
    /// GÖRDÜĞÜ dropdown'da ön-seçili gelir; arkasında sessizce uygulanmaz. Ayrıca <c>null</c> ile
    /// "Otomatik" AYNI ŞEY DEĞİLDİR (null'da manuel ücret kazanır) — bu yüzden varsayılan null'dır.</para>
    /// </summary>
    public string? VarsayilanFiyatTuru { get; set; }

    /// <summary>
    /// Kira teslim (çıkış) formundaki "Çıkış Yakıt" alanının varsayılanı, 0-12 skalası
    /// (null = bugünkü sabit <c>8</c>).
    ///
    /// <para>UYGULANIYOR mu: evet, ama yalnız FORM ÖN-DOLDURMA olarak — teslim eden operatör değeri
    /// görür ve değiştirebilir. Kaydedilen <c>RentalContract.CikisYakit</c> hâlâ formdan gelir.</para>
    /// </summary>
    public int? VarsayilanYakitSeviyesi { get; set; }

    /// <summary>
    /// BEKLEMEDE (saklanır, hiçbir hesap okumaz): "drop mesafesi tanımlı değilse drop ücreti 0 kabul
    /// edilsin mi". Bugün <c>FeeLineService.DropUcretCozAsync</c> eşleşen <c>DropTanim</c> yoksa
    /// sözleşmedeki elle girilen <c>DropUcreti</c>'ni kullanır. Bu anahtarı motora bağlamak PARA
    /// davranışını değiştirir (elle girilmiş ücreti sıfırlar) → ayrı bir para fazı + adversarial
    /// inceleme ister. Şimdilik yalnız tenant'ın niyetini saklar.
    /// </summary>
    public bool? DropMesafeYokIseSifir { get; set; }

    /// <summary>
    /// BEKLEMEDE (saklanır, hiçbir hesap okumaz): geç dönüşte uzatma günü sayılmadan önce tanınacak
    /// tolerans (DAKİKA).
    ///
    /// <para>DİKKAT — KARIŞTIRMAYIN: <c>BookingMath.KismiGunEsigiSaat = 3.0</c> ile ALAKASI YOKTUR.
    /// O sabit GÜN SAYIMI içindir (kira başında kaç gün faturalanacağı) ve 2026-08-06 canlı
    /// kalibrasyonuyla ölçülmüştür (<c>CanliGunKalibrasyonTests</c> kilidi). Bu alan ise
    /// <c>ReturnMath</c>'in geç-dönüş uzatması içindir; orası bugün SAF <c>ceil</c>'dir, toleransı
    /// yoktur. Bu faz <c>KismiGunEsigiSaat</c>'e DOKUNMAZ.</para>
    /// </summary>
    public int? SaatFarkiToleransDk { get; set; }

    /// <summary>
    /// BEKLEMEDE (saklanır, hiçbir hesap okumaz): iade/dönüş işleminin yapılabileceği saat sınırı.
    /// Birimi ve tam semantiği canlı sistemden BİREBİR doğrulanmadı — bu yüzden yalnız negatif-olmama
    /// doğrulaması var, bir tavan/anlam kodlanmadı ve hiçbir servise bağlanmadı.
    /// </summary>
    public int? IadeIslemSaatSiniri { get; set; }

    /// <summary>
    /// UYGULANIYOR: true iken deftere giden hiçbir işlemde ELLE kur girilemez — kur daima
    /// tenant sabit kuru / TCMB'den çözülür (<c>KurCozucu</c>). Varsayılan <c>false</c> = BUGÜNKÜ
    /// davranış (açık kur aynen kabul edilir).
    ///
    /// <para>KAPSAM (dürüst sınır): kilit <c>KurCozucu.CozAsync</c> giriş noktasındadır, yani ÇİFT
    /// TARAFLI DEFTERE ulaşan tüm kur yolları (tahsilat/ödeme/virman/gider/depozito/ceza/MTV/
    /// muayene/sigorta/araç satış/dış hizmet/araç kredisi taksiti) kapsanır. Kayıtların üzerindeki
    /// BİLGİ amaçlı kur alanları (ör. <c>ServiceRecord.OdemeKur</c>, <c>AracKredi.Kur</c>,
    /// <c>Vehicle.AlimBedeliKur</c>, müşteri taksit kuru) deftere girmedikleri için kilit dışıdır.</para>
    ///
    /// <para>BEDELİ (bilinçli): kilit açıkken, ledger-only bir kaydın ORİJİNAL kurla ters çevrilmesi
    /// (ör. cari virman düzeltmesi) artık mümkün değildir — düzeltme günün kuruyla yazılır ve baz
    /// parada kalıntı bırakabilir. Ekranda bu uyarı yazılıdır.</para>
    /// </summary>
    public bool KurElleGirisKilitli { get; set; }

    // ---- FAZ-81: görünüm renk kodları (canlı ayarlar.aspx "Renk Kodları" bölümü) ----
    // Hepsi "#rrggbb" biçiminde hex ya da null. NULL = koddaki varsayılan renk kullanılır;
    // CSS tarafında fallback zinciri var → boş bırakan tenant'ta görünüm BİREBİR eskisi gibi kalır.
    /// <summary>Vadesi geçmiş / gecikmiş uyarılar (vade panosu, pano rozeti).</summary>
    public string? RenkGecikenler { get; set; }
    /// <summary>Bugün dönmesi gereken kiralar (pano Dönüşler paneli).</summary>
    public string? RenkBugunDonecekler { get; set; }
    /// <summary>Bugün çıkacak rezervasyonlar (pano Çıkışlar paneli).</summary>
    public string? RenkBugunCikacaklar { get; set; }
    /// <summary>Henüz onaylanmamış (opsiyonlu) rezervasyon.</summary>
    public string? RenkOpsiyonlu { get; set; }
    /// <summary>Risk limitini aşan cari bakiyesi.</summary>
    public string? RenkLimitBakiye { get; set; }
    /// <summary>Alacaklı cari.</summary>
    public string? RenkAlacakli { get; set; }
    /// <summary>Plakası atanmış rezervasyon.</summary>
    public string? RenkRezAtananPlaka { get; set; }
    /// <summary>Uzun süredir kiralanmayan / boştaki araç.</summary>
    public string? RenkKiralanmayan { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
