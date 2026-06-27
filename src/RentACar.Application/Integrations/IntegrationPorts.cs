namespace RentACar.Application.Integrations;

// ───────────────────────── Bildirim (Faz 1.h) ─────────────────────────

public interface ISmsService
{
    Task<bool> SendAsync(string phone, string message, CancellationToken ct = default);
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

public interface IEInvoiceService
{
    Task<EInvoiceResult> SendAsync(EInvoiceRequest request, CancellationToken ct = default);
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
