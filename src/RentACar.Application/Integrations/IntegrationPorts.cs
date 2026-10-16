namespace RentACar.Application.Integrations;

// ───────────────────────── Bildirim (Faz 1.h) ─────────────────────────

public interface ISmsService
{
    /// <param name="sender">Gönderen kimliği (alfanümerik başlık ya da numara). Tenant'ın kendi
    /// başlığı (<c>TenantSettings.SmsBaslik</c>) buraya geçer; boşsa sağlayıcı varsayılanı kullanılır.
    /// Not: alfanümerik başlık Türkiye'de operatör kaydı ister — kayıtsız başlık teslim edilmez.</param>
    Task<bool> SendAsync(string phone, string message, string? sender = null, CancellationToken ct = default);
}

// ───────────────────────── E-posta (bildirim omurgası) ─────────────────────────

/// <summary>Tek bir gönderim için çözülmüş SMTP yapılandırması (şifre DÜZ METİN — çağıran çözer).</summary>
public sealed record SmtpAyar(
    string Host, int Port, bool Ssl, string? Kullanici, string? Sifre,
    string GonderenAdres, string? GonderenAd);

public sealed record EpostaMesaj(string Alici, string Konu, string GovdeHtml, string? GovdeDuz = null);

/// <summary>Gönderim sonucu. <paramref name="Hata"/> operatöre gösterilecek Türkçe cümledir.</summary>
public sealed record EpostaSonuc(bool Ok, string? Hata);

/// <summary>
/// SMTP gönderici. Yapılandırma TENANT BAŞINA olduğu için ayar parametre olarak geçer —
/// gönderici durum tutmaz ve singleton kaydedilebilir.
/// </summary>
public interface IEmailSender
{
    Task<EpostaSonuc> SendAsync(SmtpAyar setting, EpostaMesaj message, CancellationToken ct = default);
}

public interface IWhatsAppService
{
    Task<bool> SendTemplateAsync(
        string phone, string templateName, IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct = default);
}

public sealed record CalendarEvent(string Title, DateTimeOffset Start, DateTimeOffset End, string? Description);

public interface IGoogleCalendarService
{
    /// <summary>Etkinlik oluşturur, takvim etkinlik id'sini döner.</summary>
    Task<string?> CreateEventAsync(CalendarEvent ev, CancellationToken ct = default);
}

// ───────────────────────── Finans (Faz 2) ─────────────────────────

public sealed record EInvoiceRequest(string AliciVknOrTckn, string AliciUnvan, decimal Tutar, decimal KdvTutar, string Currency);
public sealed record EInvoiceResult(bool Success, string? Ettn, string? Error);

/// <summary>GİB gelen kutusundan çekilen bir gelen e-fatura kalemi (ham).</summary>
public sealed record EInvoiceInboxItem(
    string Ettn, string GonderenVkn, string GonderenUnvan, DateTimeOffset Tarih,
    decimal NetTutar, decimal KdvTutar, decimal GenelToplam, string Currency);

public interface IEInvoiceService
{
    Task<EInvoiceResult> SendAsync(EInvoiceRequest request, CancellationToken ct = default);

    /// <summary>GİB gelen kutusundan [from,to] gelen faturaları çeker. Stub boş liste döner
    /// (entegrasyon kimliği yapılandırılana dek). Kimlik gelince gerçek adapter takılır.</summary>
    Task<IReadOnlyList<EInvoiceInboxItem>> FetchInboxAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Ödeme sayfasına gönderilecek alıcı bilgisi (sağlayıcı zorunlu alanları).</summary>
public sealed record PosAlici(
    string Id, string Ad, string Soyad, string Eposta, string Telefon,
    string KimlikNo, string Adres, string Sehir, string Ulke, string Ip);

/// <summary>
/// Barındırılan ödeme sayfası isteği.
/// </summary>
/// <param name="Provizyon">true → ön provizyon (kart bloke edilir, tutar ÇEKİLMEZ);
/// false → doğrudan tahsilat.</param>
/// <param name="Referans">Bizim tarafımızdaki iş referansı (sağlayıcıya conversationId olarak gider,
/// sonuç sorgusunda geri döner) — sözleşme/rezervasyon numarası gibi.</param>
/// <param name="DonusUrl">Müşteri ödeme sayfasından döndüğünde çağrılacak bizim ucumuz.</param>
public sealed record PosOdemeIstegi(
    decimal Tutar, string ParaBirimi, string Referans, string DonusUrl,
    PosAlici Alici, string Aciklama, bool Provizyon);

/// <summary>Ödeme sayfası açma sonucu — müşteri <paramref name="OdemeSayfasiUrl"/>'ye yönlendirilir.</summary>
public sealed record PosBaslatSonuc(bool Ok, string? Token, string? OdemeSayfasiUrl, string? Hata);

/// <summary>
/// Ödeme sayfasından dönüş sonrası SUNUCUDAN sorgulanan sonuç.
/// </summary>
/// <param name="OdemeId">Sağlayıcıdaki ödeme kimliği — kapatma/iptal bunu ister.</param>
/// <param name="IslemId">Kalem işlem kimliği — iade bunu ister (iyzico'da paymentTransactionId).</param>
public sealed record PosDurumSonuc(
    bool Ok, string? OdemeId, string? IslemId, string? Durum, decimal? Tutar,
    string? KartOzet, string? Referans, string? Hata);

public sealed record PosResult(bool Success, string? TxRef, string? Error);

/// <summary>
/// Ödeme sağlayıcısı portu — <b>BARINDIRILAN</b> ödeme sayfası modeli.
///
/// <para><b>Kart verisi bizim sunucumuza UĞRAMAZ.</b> Bu bilinçli ve kalıcı bir karardır
/// (<c>RentalContract</c>: "kart alanları PCI gereği kalıcı disabled"): müşteri sağlayıcının kendi
/// sayfasında kartını girer, biz yalnız bir jeton ve sonuç görürüz. Bu yüzden port "kart al, çek"
/// değil "sayfa aç, sonucu sor" biçimindedir — kart numarası alan bir imza PCI kapsamını
/// üstümüze alırdı.</para>
///
/// <para><b>Sonuç ASLA istemciden okunmaz:</b> müşteri dönüş adresine ne gönderirse göndersin,
/// gerçek durum <see cref="ResultAsync"/> ile SUNUCUDAN sorulur.</para>
/// </summary>
public interface IPosService
{
    /// <summary>Barındırılan ödeme sayfasını açar (provizyon ya da tahsilat).</summary>
    Task<PosBaslatSonuc> StartAsync(PosOdemeIstegi request, CancellationToken ct = default);

    /// <summary>Dönüş sonrası gerçek sonucu sağlayıcıdan sorar.</summary>
    Task<PosDurumSonuc> ResultAsync(string token, CancellationToken ct = default);

    /// <summary>Provizyonu kapatır (bloke tutarı tahsile çevirir). Kısmi tutar desteklenir.</summary>
    Task<PosResult> CloseAsync(string paymentId, decimal amount, string ip, CancellationToken ct = default);

    /// <summary>Ödemeyi/provizyonu iptal eder (aynı gün; bloke çözülür).</summary>
    Task<PosResult> CancelAsync(string paymentId, string ip, CancellationToken ct = default);

    /// <summary>Tahsil edilmiş tutarı iade eder (kısmi olabilir).</summary>
    Task<PosResult> RefundAsync(string transactionId, decimal amount, string ip, CancellationToken ct = default);
}

// ───────────────────────── Regülasyon (Faz 3) ─────────────────────────

public sealed record KabisBildirim(string SozlesmeNo, string Plaka, string TcKimlik, DateTimeOffset BasTar, DateTimeOffset BitTar);

/// <summary>
/// Tek gönderim için çözülmüş KABİS kimliği (şifre DÜZ METİN — çağıran <c>ISecretProtector</c> ile çözer, yalnız
/// bellekte taşınır). Kimlik TENANT BAŞINA olduğu için parametre olarak geçer: adaptör durum tutmaz, singleton olabilir.
/// </summary>
public sealed record KabisKimlik(RentACar.Domain.Enums.KabisAuthority Kurum, string Kullanici, string Sifre)
{
    /// <summary>Şifre kayıt yazdırımlarında (log, hata, ToString) ASLA görünmez.</summary>
    public override string ToString() => $"KabisKimlik {{ Kurum = {Kurum}, Kullanici = {Kullanici}, Sifre = *** }}";
}

/// <summary>Bildirimdeki kişi (kiralayan ya da sürücü). Kimlik numaraları YALNIZ bellekte, gönderim süresince.</summary>
/// <param name="TcKimlik">T.C. vatandaşı için 11 hane; yabancı uyrukluda boş.</param>
/// <param name="PasaportNo">TC yoksa zorunlu (yabancı uyruklu).</param>
/// <param name="Uyruk">Carideki <c>Ulke</c> alanı; boşsa boş gider (T.C. varsayılmaz).</param>
public sealed record KabisKisi(
    string? Ad, string? Soyad, string? TcKimlik, string? PasaportNo, string? Uyruk, DateOnly? DogumTarihi,
    string? EhliyetNo, string? EhliyetSinifi, DateOnly? EhliyetTarihi, string? Telefon, string? Adres)
{
    /// <summary>Kimlik/ehliyet numaraları kayıt yazdırımlarında görünmez (KVKK).</summary>
    public override string ToString() => $"KabisKisi {{ Ad = {Ad}, Soyad = {Soyad}, Kimlik = *** }}";
}

/// <summary>
/// Kiralama (teslim) bildirimi yükü. Alan listesi kanunun "kiralayan şahıs ile kiralanan araç bilgileri" ifadesinden
/// türetildi; karşı sistemin RESMÎ alan sözlüğü kamuya açık değildir (<c>docs/entegrasyon/kabis.md</c>,
/// "doğrulanamadı"). Gerçek adaptör bu yükü kendi şemasına eşler.
/// </summary>
/// <param name="Referans">Bizim tarafımızdaki tekil sözleşme numarası (sorgu/iptal anahtarı olarak da gider).</param>
/// <param name="KurumMu">Kiralayan kurumsal cari: kamu kurumunda kanun gereği yalnız sözleşme + araç bilgisi gider.</param>
public sealed record KabisKiralamaBildirimi(
    string Referans, string Plaka, string? AracMarkaModel,
    KabisKisi? Kiralayan, KabisKisi? Surucu, bool KurumMu, string? KurumUnvan, string? KurumVergiNo,
    DateTimeOffset TeslimUtc, DateTimeOffset PlanlananDonusUtc, string? TeslimYeri, string? DonusYeri)
{
    /// <summary>Yük kayıt yazdırımlarında yalnız referans + plaka görünür.</summary>
    public override string ToString() => $"KabisKiralamaBildirimi {{ Referans = {Referans}, Plaka = {Plaka} }}";
}

/// <summary>İade (dönüş) bildirimi yükü (kimlik numarası taşımaz).</summary>
public sealed record KabisIadeBildirimi(
    string Referans, string? DisReferans, string Plaka, DateTimeOffset IadeUtc, string? IadeYeri, int? DonusKm);

/// <summary>Karşı sistemin cevabının sınıfı — kuyruk durumu buna göre ilerler.</summary>
public enum KabisSonucTuru
{
    /// <summary>Kabul edildi; <see cref="KabisSonuc.DisReferans"/> doludur.</summary>
    Basarili,
    /// <summary>Adaptör/kimlik yapılandırılmadı — kayıt "bağlı değil" kalır, deneme hakkı YAKILMAZ.</summary>
    BagliDegil,
    /// <summary>Geçici (ağ, zaman aşımı, 5xx, oturum) — geri çekilmeyle yeniden denenir.</summary>
    GeciciHata,
    /// <summary>Kalıcı (veri reddi, yetkisiz kullanıcı) — operatör düzeltmeden denenmez.</summary>
    KaliciHata,
}

/// <summary>Bildirim/iptal sonucu. <paramref name="Hata"/> operatöre gösterilecek Türkçe cümle (çağıran ayrıca maskeler).</summary>
public sealed record KabisSonuc(KabisSonucTuru Tur, string? DisReferans, string? Hata)
{
    public bool Basarili => Tur == KabisSonucTuru.Basarili;
}

/// <summary>Sorgu sonucu: karşı sistemde bu referansla bir kayıt var mı, durumu ne.</summary>
public sealed record KabisSorguSonucu(KabisSonucTuru Tur, bool KayitVar, string? Durum, string? Hata);

/// <summary>
/// KABİS portu. Yasal dayanak 1774 s. Kimlik Bildirme Kanunu ek md. 3 (bkz. <c>docs/entegrasyon/kabis.md</c>).
///
/// <para><b>DÜRÜST STUB KURALI:</b> yapılandırma yokken hiçbir yöntem başarı DÖNMEZ — bildirilmemiş kiralamayı
/// "bildirildi" göstermek, teslimden önce bildirmemenin idari para cezasını (Ek md. 3/4) gizler. Stub <see cref="KabisSonucTuru.BagliDegil"/>
/// döner; kuyruk kaydı "bağlı değil" notuyla <c>Bekliyor</c> kalır.</para>
///
/// <para><b>PII:</b> yük kayıtları kimlik numarası taşır; adaptör onları loglamaz, hata metnine koymaz. Çağıran
/// (<c>KabisNotificationService</c>) dönen hata metnini yine de maskeleyip saklar (savunma derinliği).</para>
/// </summary>
public interface IKabisService
{
    /// <summary>ESKİ tek-alanlı imza (v1 stub). Yeni akış <see cref="NotifyRentalAsync"/>'i kullanır; stub false döner.</summary>
    Task<bool> NotifyAsync(KabisBildirim notification, CancellationToken ct = default);

    /// <summary>Teslim anında kiralayan + araç bildirimi.</summary>
    Task<KabisSonuc> NotifyRentalAsync(KabisKimlik credentials, KabisKiralamaBildirimi notice, CancellationToken ct = default);

    /// <summary>İade (dönüş) bildirimi.</summary>
    Task<KabisSonuc> NotifyReturnAsync(KabisKimlik credentials, KabisIadeBildirimi notice, CancellationToken ct = default);

    /// <summary>Bildirilmiş kiralamanın iptali.</summary>
    Task<KabisSonuc> CancelAsync(KabisKimlik credentials, string reference, string? externalReference, string reason,
        CancellationToken ct = default);

    /// <summary>Referansla karşı sistemde kayıt sorgusu (belirsiz sonuçlu denemeden sonra mükerrer gönderimi önlemek için).</summary>
    Task<KabisSorguSonucu> QueryAsync(KabisKimlik credentials, string reference, CancellationToken ct = default);
}

public sealed record TollCrossing(DateTimeOffset Zaman, string Gecis, decimal Tutar);

public interface IHgsService
{
    Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(
        string plate, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
