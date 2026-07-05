using RentACar.Web.Kur;

namespace RentACar.Web.Jobs;

/// <summary>
/// Scheduler: TCMB günlük döviz kurunu periyodik çeker (açılıştan ~20sn sonra + her 6 saatte). Kimliksiz
/// (public feed). Per-hata try/catch → job çökmez. today.xml her zaman EN SON yayınlananı döner (hafta sonu
/// Cuma'yı) → GetRate ≤tarih fallback ile boşluğu kapatır.
/// </summary>
public sealed class TcmbKurJob(TcmbKurService svc, ILogger<TcmbKurJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } // migration/seed bitsin
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await svc.RefreshAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; } // shutdown: hata değil
            catch (Exception ex) { log.LogError(ex, "TCMB kur taraması başarısız."); }
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
