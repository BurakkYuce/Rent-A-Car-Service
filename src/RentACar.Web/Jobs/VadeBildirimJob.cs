using Microsoft.EntityFrameworkCore;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// Scheduler: periyodik olarak (12 saatte bir + açılıştan ~30sn sonra) her tenant'ın yaklaşan/geçmiş
/// sigorta/MTV/muayene vadelerini tarayıp kalıcı uygulama-içi bildirim üretir (idempotent) + firma sahibine
/// günlük operasyon özeti WhatsApp gönderir (saat kapılı, idempotent). Dış e-posta YOK; WhatsApp config-gated
/// (yoksa stub no-op). racar_app bağlantısı + tenant-loop + GUC (backfill deseni; owner DEĞİL).
/// Her üretici adımı YALITILMIŞTIR (<see cref="GeneratorIsolation"/>): biri patlarsa diğerleri ve WhatsApp
/// özeti yine koşar (#265'te müşteri hatırlatmalarının hatası özeti de her tenant için düşürüyordu).
/// </summary>
public sealed class VadeBildirimJob(
    IConfiguration config, IWhatsAppService whatsapp,
    RentACar.Application.Common.ISecretProtector secrets,
    IEmailSender eposta, ISmsService sms,
    RentACar.Application.Reporting.TutSatEsikleri tutSatEsik, ILogger<VadeBildirimJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);
    // Saat dilimi TEK kaynaktan: aynı çözüm mantığı üç ayrı yerde kopyalanmıştı (iki job +
    // belge numarası). Numaradaki gün ile job'un günü ayrışmasın diye ortaklaştırıldı.
    private static readonly TimeZoneInfo Tz = RentACar.Infrastructure.Persistence.TenantDay.Slice;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } // migration/seed bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; } // shutdown: hata değil (F3)
            catch (Exception ex) { log.LogError(ex, "Vade bildirim taraması başarısız."); RentACar.Application.Observability.RacarMetrics.JobFailed("vade-bildirim"); }
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

        // Tenant listesi (platform tablosu; racar_app SELECT açık).
        List<Guid> tenantIds;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
            tenantIds = await db0.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var toplam = 0;
        foreach (var tenantId in tenantIds)
            toplam += await TenantKosAsync(options, tenantId, DbAdimlari(tenantId, now),
                OzetAdimi(options, tenantId, now), log, ct);
        if (toplam > 0) log.LogInformation("Vade bildirim: {Count} yeni bildirim üretildi.", toplam);
    }

    /// <summary>
    /// Tenant başına DB'li adımlar — sıra sabit: vade → filo (bakım-km + tut/sat) → müşteri
    /// hatırlatmaları (yarın teslim / bugün iade + kuyruk yeniden denemesi). <c>public</c>: test,
    /// job'ın GERÇEK adım listesini alıp birini patlayanla değiştirerek yalıtımı sınar.
    /// </summary>
    public IReadOnlyList<UreticiAdimi> DbAdimlari(Guid tenantId, DateTimeOffset now) =>
    [
        new(JobRunRecorder.DueNotification, (db, ct) => DueNotificationGenerator.RunAsync(db, tenantId, now, ct)),
        new(JobRunRecorder.FleetNotification, (db, ct) => FleetNotificationGenerator.RunAsync(db, tenantId, now, tutSatEsik, ct)),
        new(JobRunRecorder.CustomerNotification, (db, ct) =>
            CustomerNotificationGenerator.RunAsync(db, tenantId, now, Tz, secrets, eposta, sms, ct)),
    ];

    /// <summary>Günlük operasyon özeti WhatsApp adımı (kendi 2 kısa context'i; saat-kapılı + idempotent; stub→no-op).</summary>
    public Func<CancellationToken, Task> OzetAdimi(DbContextOptions<AppDbContext> options, Guid tenantId, DateTimeOffset now)
        => ct => WhatsAppSummarySender.SendDailyAsync(options, tenantId, whatsapp, now, Tz, log, ct);

    /// <summary>
    /// Bir tenant'ın koşusu: her adım KENDİ context'i ve try/catch'i ile (<see cref="GeneratorIsolation"/>).
    /// Bir üreticinin hatası sonrakileri ve WhatsApp özetini DÜŞÜRMEZ; log hangi üreticinin hangi
    /// tenant için patladığını söyler, koşu günlüğüne Basarisiz satırı düşer, metrik artar.
    /// </summary>
    public static async Task<int> TenantKosAsync(
        DbContextOptions<AppDbContext> options, Guid tenantId, IEnumerable<UreticiAdimi> dbAdimlari,
        Func<CancellationToken, Task> ozetAdimi, ILogger log, CancellationToken ct)
    {
        var toplam = 0;
        foreach (var adim in dbAdimlari)
            toplam += await GeneratorIsolation.DbStepAsync(options, tenantId, adim.Ad,
                db => adim.Uret(db, ct), log, n => n, ct: ct);
        await GeneratorIsolation.StepAsync(tenantId, JobRunRecorder.WhatsAppSummary, () => ozetAdimi(ct), log, ct);
        return toplam;
    }
}
