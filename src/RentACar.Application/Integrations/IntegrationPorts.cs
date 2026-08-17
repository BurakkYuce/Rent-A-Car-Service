namespace RentACar.Application.Integrations;

// ───────────────────────── Bildirim (Faz 1.h) ─────────────────────────

public interface ISmsService
{
    /// <param name="gonderen">Gönderen kimliği (alfanümerik başlık ya da numara). Tenant'ın kendi
    /// başlığı (<c>TenantSettings.SmsBaslik</c>) buraya geçer; boşsa sağlayıcı varsayılanı kullanılır.
    /// Not: alfanümerik başlık Türkiye'de operatör kaydı ister — kayıtsız başlık teslim edilmez.</param>
    Task<bool> SendAsync(string phone, string message, string? gonderen = null, CancellationToken ct = default);
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
    Task<EpostaSonuc> SendAsync(SmtpAyar ayar, EpostaMesaj mesaj, CancellationToken ct = default);
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
/// gerçek durum <see cref="SonucAsync"/> ile SUNUCUDAN sorulur.</para>
/// </summary>
public interface IPosService
{
    /// <summary>Barındırılan ödeme sayfasını açar (provizyon ya da tahsilat).</summary>
    Task<PosBaslatSonuc> BaslatAsync(PosOdemeIstegi istek, CancellationToken ct = default);

    /// <summary>Dönüş sonrası gerçek sonucu sağlayıcıdan sorar.</summary>
    Task<PosDurumSonuc> SonucAsync(string token, CancellationToken ct = default);

    /// <summary>Provizyonu kapatır (bloke tutarı tahsile çevirir). Kısmi tutar desteklenir.</summary>
    Task<PosResult> KapatAsync(string odemeId, decimal tutar, string ip, CancellationToken ct = default);

    /// <summary>Ödemeyi/provizyonu iptal eder (aynı gün; bloke çözülür).</summary>
    Task<PosResult> IptalAsync(string odemeId, string ip, CancellationToken ct = default);

    /// <summary>Tahsil edilmiş tutarı iade eder (kısmi olabilir).</summary>
    Task<PosResult> IadeAsync(string islemId, decimal tutar, string ip, CancellationToken ct = default);
}

// ───────────────────────── Regülasyon (Faz 3) ─────────────────────────

public sealed record KabisBildirim(string SozlesmeNo, string Plaka, string TcKimlik, DateTimeOffset BasTar, DateTimeOffset BitTar);

public interface IKabisService
{
    /// <summary>Kira sözleşmesi emniyet bildirimi.</summary>
    Task<bool> BildirAsync(KabisBildirim bildirim, CancellationToken ct = default);
}

public sealed record TollCrossing(DateTimeOffset Zaman, string Gecis, decimal Tutar);

public interface IHgsService
{
    Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(
        string plaka, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
