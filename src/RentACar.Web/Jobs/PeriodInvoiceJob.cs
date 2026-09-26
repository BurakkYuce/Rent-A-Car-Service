using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// Scheduler (FAZ 4.2-B4): günde bir (+ açılıştan ~45sn sonra) tenant'ları dolaşıp vadesi gelmiş
/// fatura dönemlerini otomatik keser (DonemFaturaUretici; ayar-kapılı — DonemselFaturalamaJob).
/// VadeBildirimJob iskeleti: racar_app bağlantısı + tenant-loop + GUC; per-tenant hata izolasyonu
/// (bir tenant'ın hatası döngüyü çökertmez — loglanır, devam edilir). İdempotent: kesilen dönem
/// Kesildi olduğundan ikinci koşu no-op; kilitli muhasebe dönemi/FX kiralar loglanıp atlanır.
/// </summary>
public sealed class PeriodInvoiceJob(IConfiguration config, ILogger<PeriodInvoiceJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), ct); } // migration/seed bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Dönem faturası job koşusu başarısız."); RentACar.Application.Observability.RacarMetrics.JobFailed("donem-fatura"); }
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        // F8.1a R2-L2: iş context'i de defter baz tutarı son savunmasını alır (DI dışı seçenek kurucusu).
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn)
            .AddInterceptors(new RentACar.Infrastructure.Persistence.Interceptors.LedgerAmountGuardInterceptor()).Options;

        List<Guid> tenantIds;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
            tenantIds = await db0.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var tenantId in tenantIds)
        {
            // Per-tenant yalıtım (UreticiYalitimi): kendi context'i + koşu günlüğü (başarı VE hata);
            // hata loglanır (hangi iş, hangi tenant) + metrik, sonraki tenant etkilenmez.
            var result = await GeneratorIsolation.DbStepAsync(options, tenantId, JobRunRecorder.PeriodInvoice,
                db => PeriodInvoiceGenerator.RunAsync(db, tenantId, now, ct),
                log,
                s => s.Kesilen,
                s => s.Atlananlar.Count == 0 ? null : string.Join(" | ", s.Atlananlar), ct);
            if (result is not null && (result.Kesilen > 0 || result.Atlananlar.Count > 0))
                log.LogInformation("Dönem job tenant {Tenant}: {Kesilen} kesim, {Tahsilat} tahsilat; atlanan: {Atlanan}",
                    tenantId, result.Kesilen, result.Tahsilat, string.Join(" | ", result.Atlananlar));
        }
    }
}
