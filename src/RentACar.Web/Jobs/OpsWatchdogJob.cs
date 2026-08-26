using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// Grafana'dan BAĞIMSIZ uygulama-içi kritik-alarm scheduler'ı: Collector/Grafana çökse bile çalışır (dış
/// gözlem yığını gerekmez). Periyodik (varsayılan 1 saat) iki sinyali kontrol eder — TCMB kur bayatlığı +
/// tekrarlayan job hatası (mevcut <c>racar.job.fail.total</c> sayacından MeterListener ile; job'lara dokunmaz)
/// — ve <c>ops_alert</c> WhatsApp şablonuyla ops telefonuna gönderir (PR3 <c>/internal/alert</c> ile AYNI
/// hedef/şablon). Config-gated: <c>Twilio:AlertPhone</c> + <c>Twilio:Templates:ops_alert</c> yoksa PASİF.
/// İdempotency: kur uyarısı İstanbul-günü başına bir kez (bayatlık sürdükçe günde bir); job uyarısı delta-tabanlı.
/// </summary>
public sealed class OpsWatchdogJob(
    IConfiguration config, IWhatsAppService whatsapp, ILogger<OpsWatchdogJob> log) : BackgroundService
{
    // Saat dilimi TEK kaynaktan: aynı çözüm mantığı üç ayrı yerde kopyalanmıştı (iki job +
    // belge numarası). Numaradaki gün ile job'un günü ayrışmasın diye ortaklaştırıldı.
    private static readonly TimeZoneInfo Tz = RentACar.Infrastructure.Persistence.TenantGun.Dilim;

    // MeterListener'ın biriktirdiği kümülatif job-hata sayıları (job → toplam) ve en son alarm verilen sayı.
    private readonly ConcurrentDictionary<string, long> _jobFail = new();
    private readonly Dictionary<string, long> _jobFailAlarm = new();
    private DateOnly? _kurAlarmGun;   // kur bayat uyarısının en son verildiği İstanbul günü (bayatlık geçince sıfırlanır)
    private MeterListener? _listener;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var phone = config["Twilio:AlertPhone"];
        var template = config["Twilio:Templates:ops_alert"];
        if (!OpsWatchdog.Aktif(phone, template, config["OpsWatchdog:Enabled"]))
        {
            log.LogInformation("Ops-watchdog PASİF (Twilio:AlertPhone + Twilio:Templates:ops_alert tanımlı değil " +
                               "veya OpsWatchdog:Enabled=false).");
            return; // yapılandırılmamış → hiç döngü yok
        }

        var interval = TimeSpan.FromHours(Math.Max(0.1, ReadDouble("OpsWatchdog:IntervalHours", 1)));
        var kurEsik = ReadInt("OpsWatchdog:KurBayatGun", 5); // hafta sonu/tatil'i absorbe eder (TCMB iş-günü yayını)
        var jobEsik = ReadInt("OpsWatchdog:JobHataEsik", 2);
        StartMeterListener();
        log.LogInformation("Ops-watchdog AKTİF (aralık {Interval}, kur-bayat eşiği {Kur}g, job-hata eşiği {Job}).",
            interval, kurEsik, jobEsik);

        try { await Task.Delay(TimeSpan.FromSeconds(60), ct); } // migration/seed + ilk kur çekimi bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(phone!, kurEsik, jobEsik, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; } // shutdown: hata değil
            catch (Exception ex) { log.LogError(ex, "Ops-watchdog taraması başarısız."); }
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(string phone, int kurEsik, int jobEsik, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var mesajlar = new List<string>();

        // 1) TCMB kur bayatlığı (paylaşımlı tablo; NullTenantContext ile oku, GUC gerekmez).
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;
        DateTimeOffset? sonKur;
        await using (var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
            sonKur = await OpsWatchdog.SonKurTarihiAsync(db, ct);

        var ist = TimeZoneInfo.ConvertTime(now, Tz);
        var bugun = DateOnly.FromDateTime(ist.DateTime);
        if (OpsWatchdog.KurBayatMi(sonKur, now, kurEsik))
        {
            if (_kurAlarmGun != bugun) // günde bir (bayatlık sürdükçe her gün tekrar)
            {
                mesajlar.Add(OpsWatchdog.KurMesaji(sonKur!.Value, now));
                _kurAlarmGun = bugun;
            }
        }
        else _kurAlarmGun = null; // bayatlık geçti → bir sonraki bayatlıkta yeniden uyar

        // 2) Tekrarlayan job hatası (kümülatif delta; MeterListener'dan).
        foreach (var kv in _jobFail)
        {
            var job = kv.Key;
            var guncel = kv.Value;
            var sonAlarm = _jobFailAlarm.GetValueOrDefault(job, 0);
            if (OpsWatchdog.JobHataAlarmi(guncel, sonAlarm, jobEsik))
            {
                mesajlar.Add(OpsWatchdog.JobMesaji(job, guncel - sonAlarm));
                _jobFailAlarm[job] = guncel;
            }
        }

        foreach (var mesaj in mesajlar)
        {
            log.LogWarning("OPS WATCHDOG: {Mesaj}", mesaj); // Grafana'sız da kalıcı log (Loki/dosya/console)
            try
            {
                await whatsapp.SendTemplateAsync(phone, "ops_alert",
                    new Dictionary<string, string> { ["1"] = mesaj }, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "Ops-watchdog WhatsApp gönderilemedi."); } // alarm-forward hatası alarmı düşürmez
        }
    }

    /// <summary>Aynı süreçteki <c>racar.job.fail.total</c> sayacını dinle → job'lara dokunmadan hata sinyali.
    /// Birden çok MeterListener bir arada çalışır (OTel'inki + bu) — çakışma yok.</summary>
    private void StartMeterListener()
    {
        _ = RentACar.Application.Observability.RacarMetrics.Meter; // static init'i tetikle (instrument'lar yayınlansın)
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == "RentACar" && inst.Name == "racar.job.fail.total")
                    l.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            var job = "?";
            foreach (var t in tags)
                if (t.Key == "job") { job = t.Value?.ToString() ?? "?"; break; }
            _jobFail.AddOrUpdate(job, measurement, (_, old) => old + measurement);
        });
        listener.Start();
        _listener = listener;
    }

    public override void Dispose()
    {
        _listener?.Dispose();
        base.Dispose();
    }

    private double ReadDouble(string key, double dflt) =>
        double.TryParse(config[key], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : dflt;
    private int ReadInt(string key, int dflt) =>
        int.TryParse(config[key], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : dflt;
}
