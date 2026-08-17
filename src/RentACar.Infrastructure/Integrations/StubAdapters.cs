using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Integrations;

namespace RentACar.Infrastructure.Integrations;

// v1 STUB adapter'lar: gerçek entegrasyon (e-Fatura/POS/KABIS/HGS/SMS/WhatsApp/Calendar)
// bu port'ların arkasına kademeli olarak gelir.
//
// KURAL (dürüst stub): yapılandırma YOKKEN bir stub ASLA "başarılı" dönmez. Sahte başarı, çağıranın
// "gitti" sanıp kalıcı kayda (fatura ETTN'i, bildirim izi, provizyon kaydı) yanlış yazmasına yol
// açar — e-Fatura stub'ında bu bilinçli olarak zaten böyleydi (M2), aynı kural SMS/POS/KABİS'e de
// uygulandı. Boş liste dönenler (HGS, e-Fatura gelen kutusu) zaten dürüsttür: "veri yok" ≠ "başarı".
public sealed class StubSmsService : ISmsService
{
    // Sahte başarı YOK: SMS sağlayıcısı yapılandırılmadıysa mesaj GİTMEZ, çağıran bunu görmelidir.
    public Task<bool> SendAsync(string phone, string message, string? gonderen = null, CancellationToken ct = default)
        => Task.FromResult(false);
}

/// <summary>SMTP yapılandırılmadığında devreye giren gönderici — sessizce başarı dönmez.</summary>
public sealed class NoopEmailSender : IEmailSender
{
    public Task<EpostaSonuc> SendAsync(SmtpAyar ayar, EpostaMesaj mesaj, CancellationToken ct = default)
        => Task.FromResult(new EpostaSonuc(false, "E-posta göndericisi yapılandırılmadı."));
}

public sealed class StubWhatsAppService : IWhatsAppService
{
    public Task<bool> SendTemplateAsync(
        string phone, string templateName, IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
        => Task.FromResult(true);
}

public sealed class StubGoogleCalendarService : IGoogleCalendarService
{
    public Task<string?> CreateEventAsync(CalendarEvent ev, CancellationToken ct = default)
        => Task.FromResult<string?>("stub-event-" + Guid.NewGuid().ToString("N"));
}

public sealed class StubEInvoiceService : IEInvoiceService
{
    // Stub GERÇEKTEN GÖNDERMEZ → Success=false (adversarial M2): aksi halde InvoiceService faturayı sahte ETTN ile
    // "e-Fatura gönderildi" (EFaturaGonderildi=true) işaretliyordu — immutable kayıtta yasal/denetim açısından
    // yanıltıcı. Gerçek adapter takılınca Success=true döner → o zaman işaretlenir. Fatura yine kesilir/postlanır.
    public Task<EInvoiceResult> SendAsync(EInvoiceRequest request, CancellationToken ct = default)
        => Task.FromResult(new EInvoiceResult(false, Ettn: null, Error: "e-Fatura entegrasyonu yapılandırılmadı (stub)"));

    // Stub gelen kutusu BOŞ döner (GİB kimliği yapılandırılmadı). Gerçek adapter takılınca gerçek liste gelir;
    // GelenEFaturaService bunları ETTN'e göre upsert eder → kimlik-flip yeterli, çağrı yolu hazır.
    public Task<IReadOnlyList<EInvoiceInboxItem>> FetchInboxAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EInvoiceInboxItem>>([]);
}

public sealed class StubPosService : IPosService
{
    // Sahte "STUBTX-…" referansı ÜRETMEZ: o referans provizyon/tahsilat kaydına yazılsa, hiçbir kart
    // bloke edilmemişken sistemde geçerli bir işlem referansı varmış gibi görünürdü. Para yolunda
    // sahte başarı, e-Fatura'daki sahte ETTN ile aynı sınıf hatadır.
    public Task<PosResult> ChargeAsync(PosCharge charge, CancellationToken ct = default) => Yok();
    public Task<PosResult> AuthorizeAsync(PosCharge charge, CancellationToken ct = default) => Yok();
    public Task<PosResult> CaptureAsync(string txRef, decimal amount, CancellationToken ct = default) => Yok();
    public Task<PosResult> RefundAsync(string txRef, decimal amount, CancellationToken ct = default) => Yok();
    private static Task<PosResult> Yok()
        => Task.FromResult(new PosResult(false, TxRef: null, Error: "Ödeme sağlayıcısı yapılandırılmadı (stub)."));
}

public sealed class StubKabisService : IKabisService
{
    // KABİS bildirimi YASAL yükümlülük (1774 sayılı Kanun). Yapılandırma yokken "bildirildi" demek,
    // bildirilmemiş kiralamayı bildirilmiş göstermek olur — cezası kiralama BAŞINA işler.
    public Task<bool> BildirAsync(KabisBildirim bildirim, CancellationToken ct = default) => Task.FromResult(false);
}

public sealed class StubHgsService : IHgsService
{
    public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(
        string plaka, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TollCrossing>>([]);
}

public static class IntegrationStubs
{
    /// <summary>v1 stub adapter'larını kaydeder. Gerçek impl'ler kademeli olarak değiştirilir.</summary>
    public static IServiceCollection AddIntegrationStubs(this IServiceCollection services)
    {
        services.AddSingleton<ISmsService, StubSmsService>();
        services.AddSingleton<IEmailSender, NoopEmailSender>();
        services.AddSingleton<IWhatsAppService, StubWhatsAppService>();
        services.AddSingleton<IGoogleCalendarService, StubGoogleCalendarService>();
        services.AddSingleton<IEInvoiceService, StubEInvoiceService>();
        services.AddSingleton<IPosService, StubPosService>();
        services.AddSingleton<IKabisService, StubKabisService>();
        services.AddSingleton<IHgsService, StubHgsService>();
        return services;
    }
}
