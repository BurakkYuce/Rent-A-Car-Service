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

public sealed record PosCharge(decimal Amount, string Currency, string CardToken, bool ThreeD);
public sealed record PosResult(bool Success, string? TxRef, string? Error);

public interface IPosService
{
    Task<PosResult> ChargeAsync(PosCharge charge, CancellationToken ct = default);
    /// <summary>Provizyon (depozit hold).</summary>
    Task<PosResult> AuthorizeAsync(PosCharge charge, CancellationToken ct = default);
    Task<PosResult> CaptureAsync(string txRef, decimal amount, CancellationToken ct = default);
    Task<PosResult> RefundAsync(string txRef, decimal amount, CancellationToken ct = default);
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
