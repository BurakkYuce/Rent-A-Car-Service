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
public sealed class DonemFaturaJob(IConfiguration config, ILogger<DonemFaturaJob> log) : BackgroundService
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
            catch (Exception ex) { log.LogError(ex, "Dönem faturası job koşusu başarısız."); }
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;

        List<Guid> tenantIds;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
            tenantIds = await db0.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                var sys = new SystemTenantContext { TenantId = tenantId };
                await using var db = new AppDbContext(options, sys, sys);
                await TenantGuc.OpenAsync(db, tenantId, ct);
                var sonuc = await DonemFaturaUretici.RunAsync(db, tenantId, now, ct);
                if (sonuc.Kesilen > 0 || sonuc.Atlananlar.Count > 0)
                    log.LogInformation("Dönem job tenant {Tenant}: {Kesilen} kesim, {Tahsilat} tahsilat; atlanan: {Atlanan}",
                        tenantId, sonuc.Kesilen, sonuc.Tahsilat, string.Join(" | ", sonuc.Atlananlar));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Per-tenant izolasyon: tek tenant'ın hatası diğerlerini engellemesin.
                log.LogError(ex, "Dönem job tenant {Tenant} hatası.", tenantId);
            }
        }
    }
}
