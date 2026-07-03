using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// Scheduler: periyodik olarak (12 saatte bir + açılıştan ~30sn sonra) her tenant'ın yaklaşan/geçmiş
/// sigorta/MTV/muayene vadelerini tarayıp kalıcı uygulama-içi bildirim üretir (idempotent). Dış
/// entegrasyon/e-posta YOK. racar_app bağlantısı + tenant-loop + GUC (backfill deseni; owner DEĞİL).
/// </summary>
public sealed class VadeBildirimJob(IConfiguration config, ILogger<VadeBildirimJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } // migration/seed bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; } // shutdown: hata değil (F3)
            catch (Exception ex) { log.LogError(ex, "Vade bildirim taraması başarısız."); }
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;

        // Tenant listesi (platform tablosu; racar_app SELECT açık).
        List<Guid> tenantIds;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
            tenantIds = await db0.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var toplam = 0;
        foreach (var tenantId in tenantIds)
        {
            // Per-tenant izolasyon (adversarial F1): bir tenant'ın hatası tüm tarama döngüsünü
            // ÇÖKERTMESİN — logla, sonraki tenant'a devam et.
            try
            {
                var sys = new SystemTenantContext { TenantId = tenantId };
                await using var db = new AppDbContext(options, sys, sys);
                await db.Database.OpenConnectionAsync(ct); // GUC bağlantı ömrünce açık kalmalı
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.tenant_id', {tenantId.ToString()}, false)", ct);
                toplam += await VadeBildirimUretici.RunAsync(db, tenantId, now, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // shutdown → yukarı
            catch (Exception ex)
            {
                log.LogError(ex, "Vade bildirim: tenant {Tenant} taraması atlandı.", tenantId);
            }
        }
        if (toplam > 0) log.LogInformation("Vade bildirim: {Count} yeni bildirim üretildi.", toplam);
    }
}
