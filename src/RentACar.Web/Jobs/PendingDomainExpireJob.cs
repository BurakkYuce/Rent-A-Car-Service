using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.TenantSettings;

namespace RentACar.Web.Jobs;

/// <summary>
/// PR-5: ACME kota-kötüye-kullanım koruması — bir tenant sahibi olmadığı bir özel domain eklerse
/// (ask-endpoint 200 döner ama LE doğrulaması hiç başarılı olmaz) satır süresiz `PendingVerification`
/// kalır. Bu job periyodik olarak 48 saati geçen satırları `Failed`'e çeker. `TenantDomain` platform
/// tablosu (RLS yok) olduğu için `VadeBildirimJob`'un aksine tenant-loop/GUC GEREKMEZ — tek SQL UPDATE.
/// </summary>
public sealed class PendingDomainExpireJob(IServiceScopeFactory scopeFactory, ILogger<PendingDomainExpireJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    // F11.1b güvenlik M6: DNS TXT doğrulaması için müşteriye zaman tanınır — süre tek kaynaktan.
    private static readonly TimeSpan ExpireAfter = RentACar.Application.TenantSettings.DomainVerification.PendingLifetime;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } // migration/seed bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope(); // ITenantDomainRepository Scoped — BackgroundService (Singleton) doğrudan enjekte edemez
                var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
                var expired = await domains.ExpireOldPendingCustomDomainsAsync(DateTimeOffset.UtcNow - ExpireAfter, ct);
                if (expired > 0)
                    log.LogInformation("{Count} bekleyen özel domain süre aşımıyla Failed'e çekildi.", expired);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; } // shutdown: hata değil
            catch (Exception ex) { log.LogError(ex, "Pending domain expire taraması başarısız."); }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
