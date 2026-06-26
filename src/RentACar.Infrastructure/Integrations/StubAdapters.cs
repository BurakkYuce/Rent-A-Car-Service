using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Integrations;

namespace RentACar.Infrastructure.Integrations;

// v1 STUB adapter'lar: gerçek entegrasyon (e-Fatura/POS/KABIS/HGS/SMS/WhatsApp/Calendar)
// Faz 2/3'te bu port'ların arkasına gelir. Stub'lar başarı taklidi yapar / no-op döner →
// akışlar entegrasyon olmadan da uçtan uca çalışır ve test edilir.

public sealed class StubSmsService : ISmsService
{
    public Task<bool> SendAsync(string phone, string message, CancellationToken ct = default)
        => Task.FromResult(true);
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
    public Task<EInvoiceResult> SendAsync(EInvoiceRequest request, CancellationToken ct = default)
        => Task.FromResult(new EInvoiceResult(true, Ettn: "STUB-" + Guid.NewGuid().ToString("N"), Error: null));
}

public sealed class StubPosService : IPosService
{
    public Task<PosResult> ChargeAsync(PosCharge charge, CancellationToken ct = default) => Ok();
    public Task<PosResult> AuthorizeAsync(PosCharge charge, CancellationToken ct = default) => Ok();
    public Task<PosResult> CaptureAsync(string txRef, decimal amount, CancellationToken ct = default) => Ok();
    public Task<PosResult> RefundAsync(string txRef, decimal amount, CancellationToken ct = default) => Ok();
    private static Task<PosResult> Ok() => Task.FromResult(new PosResult(true, "STUBTX-" + Guid.NewGuid().ToString("N"), null));
}

public sealed class StubKabisService : IKabisService
{
    public Task<bool> BildirAsync(KabisBildirim bildirim, CancellationToken ct = default) => Task.FromResult(true);
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
        services.AddSingleton<IWhatsAppService, StubWhatsAppService>();
        services.AddSingleton<IGoogleCalendarService, StubGoogleCalendarService>();
        services.AddSingleton<IEInvoiceService, StubEInvoiceService>();
        services.AddSingleton<IPosService, StubPosService>();
        services.AddSingleton<IKabisService, StubKabisService>();
        services.AddSingleton<IHgsService, StubHgsService>();
        return services;
    }
}
