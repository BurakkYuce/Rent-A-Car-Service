using Microsoft.EntityFrameworkCore;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// Scheduler: periyodik olarak (12 saatte bir + açılıştan ~30sn sonra) her tenant'ın yaklaşan/geçmiş
/// sigorta/MTV/muayene vadelerini tarayıp kalıcı uygulama-içi bildirim üretir (idempotent) + firma sahibine
/// günlük operasyon özeti WhatsApp gönderir (saat kapılı, idempotent). Dış e-posta YOK; WhatsApp config-gated
/// (yoksa stub no-op). racar_app bağlantısı + tenant-loop + GUC (backfill deseni; owner DEĞİL).
/// </summary>
public sealed class VadeBildirimJob(
    IConfiguration config, IWhatsAppService whatsapp,
    RentACar.Application.Common.ISecretProtector secrets,
    IEmailSender eposta, ISmsService sms,
    RentACar.Application.Reporting.TutSatEsikleri tutSatEsik, ILogger<VadeBildirimJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);
    private static readonly TimeZoneInfo Tz = ResolveTz();

    private static TimeZoneInfo ResolveTz()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* diğerini dene */ }
        return TimeZoneInfo.Utc;
    }

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
                await using (var db = new AppDbContext(options, sys, sys))
                {
                    await TenantGuc.OpenAsync(db, tenantId, ct); // raw-context GUC açılışı (tek doğru yol)
                    // FAZ-26: koşular günlüğe yazılır (başarı VE hata). Sarmalayıcı üretici kodunu
                    // DEĞİŞTİRMEZ, dönüş/hata aynen geçer — mevcut davranış birebir korunur.
                    toplam += await JobCalismaKaydedici.CalistirAsync(db, tenantId,
                        JobCalismaKaydedici.VadeBildirim,
                        () => VadeBildirimUretici.RunAsync(db, tenantId, now, ct), n => n, ct: ct);
                    // FAZ 6.2: bakım-km (≤1000 kalan) + tut/sat (≥2 sinyal) bildirimleri — aynı kapsamlı db.
                    toplam += await JobCalismaKaydedici.CalistirAsync(db, tenantId,
                        JobCalismaKaydedici.FiloBildirim,
                        () => FiloBildirimUretici.RunAsync(db, tenantId, now, tutSatEsik, ct), n => n, ct: ct);
                    // Müşteriye giden hatırlatmalar (yarın teslim / bugün iade) + kuyrukta kalan
                    // mesajların yeniden denenmesi. Şablon tanımlı değilse mesaj KUYRUKTA kalır ve
                    // firma şablonu yazınca bu koşu onu gönderir.
                    toplam += await JobCalismaKaydedici.CalistirAsync(db, tenantId,
                        JobCalismaKaydedici.MusteriBildirim,
                        () => MusteriBildirimUretici.RunAsync(db, tenantId, now, Tz, secrets, eposta, sms, ct),
                        n => n, ct: ct);
                } // ← bağlantı KAPANIR (WhatsApp HTTP'si açık-bağlantı tutmasın)

                // Günlük operasyon özeti WhatsApp (kendi 2 kısa context'i; saat-kapılı + idempotent; stub→no-op).
                await WhatsAppOzetGonderici.SendDailyAsync(options, tenantId, whatsapp, now, Tz, log, ct);
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
