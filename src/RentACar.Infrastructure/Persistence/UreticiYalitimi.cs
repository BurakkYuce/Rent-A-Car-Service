using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentACar.Application.Observability;

namespace RentACar.Infrastructure.Persistence;

/// <summary>Tenant döngülü bir işin DB'li tek adımı: ad (koşu günlüğü + metrik etiketi) ve üretici.</summary>
public sealed record UreticiAdimi(string Ad, Func<AppDbContext, CancellationToken, Task<int>> Uret);

/// <summary>
/// Tenant döngülü arka plan işlerinde HER üretici adımını birbirinden yalıtır.
///
/// <para><b>Neden:</b> <c>VadeBildirimJob</c> üç üreticiyi aynı context'te art arda koşup WhatsApp
/// özetini de aynı try bloğunun sonuna koyuyordu. #265'te üçüncü üretici her koşuda patladığında
/// (a) WhatsApp özeti de her tenant için atlandı, (b) log yalnız "Vade bildirim: tenant … taraması
/// atlandı" dedi — HANGİ üreticinin patladığı yazmıyordu, (c) tenant düzeyindeki hata metriğe hiç
/// girmediği için <c>racar_job_fail</c> alarmı tetiklenemedi.</para>
///
/// <para><b>Neden her adıma TAZE context:</b> bir üretici patladığında paylaşılan context'te iki
/// kalıntı bırakır: kırılmış bağlantı (EF havuzdan GUC'suz bağlantı açar → sonraki üretici RLS
/// altında SESSİZCE 0 satır görür) ve izleyicide yarım varlıklar (sonraki üreticinin
/// <c>SaveChanges</c>'ı onları da yazmaya kalkar). Adım başına kısa ömürlü context ikisini de
/// yapısal olarak imkânsız kılar.</para>
/// </summary>
public static class UreticiYalitimi
{
    /// <summary>
    /// DB'li üretici adımı: kendi context'i (taze bağlantı + tenant GUC), koşu günlüğü (başarı VE
    /// hata) ve hata yalıtımı. Hata fırlatmaz — loglar, metriği artırır, <c>default</c> döner;
    /// sonraki adım etkilenmez. Yalnız kapanış iptali yukarı geçer.
    /// </summary>
    public static async Task<T?> DbAdimiAsync<T>(
        DbContextOptions<AppDbContext> options, Guid tenantId, string jobAdi,
        Func<AppDbContext, Task<T>> uret, ILogger log,
        Func<T, int?>? sayi = null, Func<T, string?>? ozet = null, CancellationToken ct = default)
    {
        try
        {
            var sys = new SystemTenantContext { TenantId = tenantId };
            await using var db = new AppDbContext(options, sys, sys);
            await TenantGuc.OpenAsync(db, tenantId, ct);
            return await JobCalismaKaydedici.CalistirAsync(db, tenantId, jobAdi, () => uret(db), sayi, ozet, ct, log);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Hata(log, ex, jobAdi, tenantId);
            return default;
        }
    }

    /// <summary>
    /// DB bağlamını KENDİSİ yöneten adım (ör. WhatsApp özeti: HTTP sırasında bağlantı tutmamak için
    /// kendi kısa context'lerini açar). Aynı yalıtım: hata loglanır + metrik, sonraki adım etkilenmez.
    /// </summary>
    public static async Task AdimAsync(
        Guid tenantId, string ad, Func<Task> adim, ILogger log, CancellationToken ct = default)
    {
        try { await adim(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Hata(log, ex, ad, tenantId); }
    }

    private static void Hata(ILogger log, Exception ex, string ad, Guid tenantId)
    {
        log.LogError(ex, "Arka plan üreticisi {Uretici} tenant {Tenant} için başarısız; sıradaki üreticiyle devam ediliyor.",
            ad, tenantId);
        RacarMetrics.JobFailed(ad);
    }
}
