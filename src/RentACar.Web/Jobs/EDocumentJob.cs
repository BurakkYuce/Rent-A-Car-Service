using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.EInvoicing;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// e-Belge gönderim işi: birkaç dakikada bir (açılıştan ~60 sn sonra başlar) tenant'ları dolaşır ve kuyruktaki
/// belgeleri entegratöre iletir (<see cref="EDocumentDispatcher"/>). PeriodInvoiceJob iskeleti: racar_app bağlantısı,
/// tenant başına yalıtım (bir tenant'ın hatası döngüyü düşürmez; koşu günlüğü + metrik).
///
/// <para>Kayıtlı entegratör adaptörü yoksa (bu sürüm) kuyrukta bekleyen belge de olmaz — fatura kesilirken kayıt
/// zaten "bağlı değil" Taslak'ıyla açılır; iş bir koşuda bir sorgu yapıp boş döner.</para>
/// </summary>
public sealed class EDocumentJob(
    IConfiguration config, EInvoiceIntegratorCatalog catalog, ISecretProtector secrets, ILogger<EDocumentJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(60), ct); } // migration/seed bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "e-Belge gönderim koşusu başarısız.");
                RentACar.Application.Observability.RacarMetrics.JobFailed(JobRunRecorder.EDocumentDispatch);
            }
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn)
            .AddInterceptors(new RentACar.Infrastructure.Persistence.Interceptors.LedgerAmountGuardInterceptor()).Options;

        List<Guid> tenantIds;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
            tenantIds = await db0.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var tenantId in tenantIds)
        {
            // Koşu günlüğü yalnız İŞ VARSA yazılır: 5 dakikalık aralıkta her tenant için boş satır, günlüğü
            // (JobCalismaLoglari) gerçek koşuları göremeyecek kadar kalabalıklaştırırdı. Ön kontrol de yalıtılmıştır.
            var hasWork = false;
            await GeneratorIsolation.StepAsync(tenantId, JobRunRecorder.EDocumentDispatch, async () =>
            {
                var sys = new SystemTenantContext { TenantId = tenantId };
                await using var probe = new AppDbContext(options, sys, sys);
                await TenantGuc.OpenAsync(probe, tenantId, ct);
                hasWork = await EDocumentDispatcher.HasDueAsync(probe, now, ct);
            }, log, ct);
            if (!hasWork) continue;

            var result = await GeneratorIsolation.DbStepAsync(options, tenantId, JobRunRecorder.EDocumentDispatch,
                db => EDocumentDispatcher.RunAsync(db, tenantId, now, catalog, secrets, log, ct),
                log,
                s => s.Gonderilen,
                s => $"gönderilen {s.Gonderilen}, bekleyen {s.Bekleyen}, hatalı {s.Hatali}, taslağa dönen {s.Taslak}", ct);
            if (result is { Gonderilen: > 0 } or { Hatali: > 0 } or { Taslak: > 0 })
                log.LogInformation("e-Belge tenant {Tenant}: {Gonderilen} gönderildi, {Bekleyen} bekliyor, {Hatali} hatalı, {Taslak} taslak.",
                    tenantId, result.Gonderilen, result.Bekleyen, result.Hatali, result.Taslak);
        }
    }
}
