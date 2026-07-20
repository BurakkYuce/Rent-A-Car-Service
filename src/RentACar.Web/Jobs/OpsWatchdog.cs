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
    public static bool Aktif(string? alertPhone, string? opsTemplate, string? enabledFlag)
    {
        if (string.Equals(enabledFlag, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.IsNullOrWhiteSpace(alertPhone) && !string.IsNullOrWhiteSpace(opsTemplate);
    }

    /// <summary>Kur bayat mı: son kur kaydı yoksa (henüz çekilmemiş/boş DB) DEĞİL (soğuk-başlangıç gürültüsü
    /// yok); aksi halde son kayıt <paramref name="esikGun"/> günden eskiyse bayat. TCMB YALNIZ iş günü yayınlar
    /// → Cuma kuru Pazartesi öğleden önce NORMALDE ~3.5 gün eskir; uzun tatil hafta sonu ~4.5 güne çıkabilir.
    /// Bu yüzden eşik bunların üstünde (varsayılan 5) seçilir → hafta sonu/tatil YANLIŞ-POZİTİF yok, gerçek
    /// (sürekli) çekim arızası yine yakalanır (arıza kalıcıdır, birkaç gün gecikme "bayat kur" uyarısı için kabul).</summary>
    public static bool KurBayatMi(DateTimeOffset? sonTarih, DateTimeOffset now, int esikGun)
    {
        if (sonTarih is null) return false;
        return (now - sonTarih.Value).TotalDays > esikGun;
    }

    /// <summary>Job hatası alarmı: bir job'ın kümülatif hata sayısı, en son alarm verilen sayıdan
    /// <paramref name="esik"/> kadar (veya fazla) arttıysa alarm. Delta-tabanlı → yalnız YENİ hatalar
    /// tetikler, kendini sınırlar (aynı hata için tekrar tekrar spam yok). Eşik ≤0 → alarm yok.</summary>
    public static bool JobHataAlarmi(long guncelToplam, long enSonAlarmVerilen, int esik)
        => esik > 0 && guncelToplam - enSonAlarmVerilen >= esik;

    public static string KurMesaji(DateTimeOffset sonTarih, DateTimeOffset now)
    {
        var gun = (int)Math.Floor((now - sonTarih).TotalDays);
        return $"UYARI: TCMB döviz kuru {gun} gündür güncellenmedi (son kayıt: {sonTarih:yyyy-MM-dd}). " +
               "Kur çekimi (TcmbKurJob) başarısız olabilir; dövizli para hesapları BAYAT kur kullanıyor olabilir.";
    }

    public static string JobMesaji(string job, long delta)
        => $"UYARI: '{job}' arka plan job'ı son kontrol penceresinde {delta} kez hata verdi. Sunucu loglarına bakın.";

    /// <summary>En yeni TCMB kur kaydının tarihi (yoksa null). Kur PAYLAŞIMLI/ulusal tablo (RLS yok) →
    /// NullTenantContext ile okunur, GUC gerekmez.</summary>
    public static async Task<DateTimeOffset?> SonKurTarihiAsync(AppDbContext db, CancellationToken ct = default)
        => await db.KurKayitlari.AsNoTracking()
            .OrderByDescending(k => k.Tarih)
            .Select(k => (DateTimeOffset?)k.Tarih)
            .FirstOrDefaultAsync(ct);
}
