using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Jobs;

/// <summary>
/// Ops-watchdog KARAR MANTIĞI (saf + test edilebilir — <see cref="OpsWatchdogJob"/> orkestratör). Grafana'dan
/// BAĞIMSIZ kritik alarm: Grafana/Collector çökse bile uygulama-içi çalışır. İki sinyal: (1) TCMB kuru bayat
/// (para hesapları bayat kur kullanır → kritik), (2) arka plan job'ı tekrarlayan hata (mevcut
/// <c>racar.job.fail.total</c> sayacından MeterListener ile okunur — job'lara dokunmaz). Config-gated:
/// yalnız <c>Twilio:AlertPhone</c> + <c>Twilio:Templates:ops_alert</c> tanımlıysa aktif (yoksa no-op).
/// </summary>
public static class OpsWatchdog
{
    /// <summary>Watchdog aktif mi: ops alarm telefonu + ops_alert şablonu tanımlı VE açıkça kapatılmamış
    /// (<c>OpsWatchdog:Enabled=false</c> zorla kapatır). Telefon yoksa gönderilecek yer yok → pasif.</summary>
    public static bool IsActive(string? alertPhone, string? opsTemplate, string? enabledFlag)
    {
        if (string.Equals(enabledFlag, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.IsNullOrWhiteSpace(alertPhone) && !string.IsNullOrWhiteSpace(opsTemplate);
    }

    /// <summary>Kur bayat mı: son kur kaydı yoksa (henüz çekilmemiş/boş DB) DEĞİL (soğuk-başlangıç gürültüsü
    /// yok); aksi halde son kayıt <paramref name="thresholdDays"/> günden eskiyse bayat. TCMB YALNIZ iş günü yayınlar
    /// → Cuma kuru Pazartesi öğleden önce NORMALDE ~3.5 gün eskir; uzun tatil hafta sonu ~4.5 güne çıkabilir.
    /// Bu yüzden eşik bunların üstünde (varsayılan 5) seçilir → hafta sonu/tatil YANLIŞ-POZİTİF yok, gerçek
    /// (sürekli) çekim arızası yine yakalanır (arıza kalıcıdır, birkaç gün gecikme "bayat kur" uyarısı için kabul).</summary>
    public static bool IsExchangeRateStale(DateTimeOffset? lastDate, DateTimeOffset now, int thresholdDays)
    {
        if (lastDate is null) return false;
        return (now - lastDate.Value).TotalDays > thresholdDays;
    }

    /// <summary>Job hatası alarmı: bir job'ın kümülatif hata sayısı, en son alarm verilen sayıdan
    /// <paramref name="threshold"/> kadar (veya fazla) arttıysa alarm. Delta-tabanlı → yalnız YENİ hatalar
    /// tetikler, kendini sınırlar (aynı hata için tekrar tekrar spam yok). Eşik ≤0 → alarm yok.</summary>
    public static bool JobErrorAlarm(long currentTotal, long lastAlerted, int threshold)
        => threshold > 0 && currentTotal - lastAlerted >= threshold;

    public static string ExchangeRateMessage(DateTimeOffset lastDate, DateTimeOffset now)
    {
        var day = (int)Math.Floor((now - lastDate).TotalDays);
        return $"UYARI: TCMB döviz kuru {day} gündür güncellenmedi (son kayıt: {lastDate:yyyy-MM-dd}). " +
               "Kur çekimi (TcmbKurJob) başarısız olabilir; dövizli para hesapları BAYAT kur kullanıyor olabilir.";
    }

    public static string JobMessage(string job, long delta)
        => $"UYARI: '{job}' arka plan job'ı son kontrol penceresinde {delta} kez hata verdi. Sunucu loglarına bakın.";

    /// <summary>En yeni TCMB kur kaydının tarihi (yoksa null). Kur PAYLAŞIMLI/ulusal tablo (RLS yok) →
    /// NullTenantContext ile okunur, GUC gerekmez.</summary>
    public static async Task<DateTimeOffset?> LastExchangeRateDateAsync(AppDbContext db, CancellationToken ct = default)
        => await db.KurKayitlari.AsNoTracking()
            .OrderByDescending(k => k.Tarih)
            .Select(k => (DateTimeOffset?)k.Tarih)
            .FirstOrDefaultAsync(ct);
}
