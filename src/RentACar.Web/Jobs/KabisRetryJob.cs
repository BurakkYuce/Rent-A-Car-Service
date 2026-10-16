using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// KABİS kuyruğu zamanlayıcısı: 5 dakikada bir (+ açılıştan ~60 sn sonra) her tenant için vadesi gelmiş bekleyen
/// bildirimleri gönderir ve iptal edilmiş kiraları uzlaştırır (<see cref="KabisNotificationGenerator"/>).
///
/// <para><b>Neden 5 dakika:</b> kanun bildirimi "anlık" ister; teslim anındaki ilk deneme istek yolunda yapılır, bu
/// iş yalnız geçici hataların geri çekilmeli tekrarını taşır (1–5–15–30–60–120–240 dk).</para>
///
/// <para><b>Gürültü:</b> önce ucuz bir ön kontrol (kapı açık mı, iş var mı) kendi kısa context'inde yapılır; koşu
/// günlüğü (<c>JobCalismaLoglari</c>) yalnız iş varken yazılır — aksi halde tenant başına günde 288 boş satır olurdu.</para>
///
/// <para>Yalıtım <see cref="GeneratorIsolation"/> ile: bir tenant'ın hatası diğerlerini düşürmez, log + metrik.
/// racar_app bağlantısı + tenant GUC (DueNotificationJob iskeleti; owner DEĞİL).</para>
/// </summary>
public sealed class KabisRetryJob(
    IConfiguration config, IKabisService port, ISecretProtector secrets, ILogger<KabisRetryJob> log) : BackgroundService
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
                log.LogError(ex, "KABİS kuyruk koşusu başarısız.");
                RentACar.Application.Observability.RacarMetrics.JobFailed(JobRunRecorder.KabisNotification);
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
            await RunTenantAsync(options, tenantId, now, port, secrets, log, ct);
    }

    /// <summary>Tek tenant: ön kontrol → iş varsa yalıtılmış adım. <c>public</c>: test gerçek yolu sınar.</summary>
    public static async Task<int> RunTenantAsync(
        DbContextOptions<AppDbContext> options, Guid tenantId, DateTimeOffset now, IKabisService port,
        ISecretProtector secrets, ILogger log, CancellationToken ct)
    {
        try
        {
            var sys = new SystemTenantContext { TenantId = tenantId };
            await using var probe = new AppDbContext(options, sys, sys);
            await TenantGuc.OpenAsync(probe, tenantId, ct);
            if (!await KabisNotificationGenerator.HasWorkAsync(probe, now, ct)) return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "KABİS ön kontrolü tenant {Tenant} için başarısız.", tenantId);
            RentACar.Application.Observability.RacarMetrics.JobFailed(JobRunRecorder.KabisNotification);
            return 0;
        }

        return await GeneratorIsolation.DbStepAsync(options, tenantId, JobRunRecorder.KabisNotification,
            db => KabisNotificationGenerator.RunAsync(db, tenantId, now, port, secrets, ct), log, n => n, ct: ct);
    }
}
