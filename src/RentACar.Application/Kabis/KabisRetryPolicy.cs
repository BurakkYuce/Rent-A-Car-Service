namespace RentACar.Application.Kabis;

/// <summary>
/// Yeniden deneme politikası (saf). Kanun bildirimi "anlık" ister; geçici hatada hızlı başlayıp seyrekleşen geri
/// çekilme uygulanır, <see cref="MaxAttempts"/> bağlantı kurulmuş denemeden sonra kayıt <c>Hata</c>'ya düşer ve
/// operatör müdahalesi bekler (kira ekranı + panel uyarısı).
/// </summary>
public static class KabisRetryPolicy
{
    /// <summary>Bağlantı kurulmuş en fazla deneme ("bağlı değil" denemeleri sayılmaz).</summary>
    public const int MaxAttempts = 8;

    /// <summary>Gönderim sahiplenme süresi: bu süre içinde aynı satırı başka bir koşu alamaz.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>"Bağlı değil" (kimlik/adaptör yok) durumunda bir sonraki bakış — bağlantı gelene dek seyrek.</summary>
    public static readonly TimeSpan NotConfiguredDelay = TimeSpan.FromMinutes(60);

    /// <summary>İade, Kiralama bildirilmeden gönderilmez; o arada bekleme aralığı.</summary>
    public static readonly TimeSpan DependencyDelay = TimeSpan.FromMinutes(15);

    private static readonly int[] DelayMinutes = [1, 5, 15, 30, 60, 120, 240];

    /// <summary><paramref name="attempt"/>'inci başarısız denemeden (1'den başlar) sonraki bekleme.</summary>
    public static TimeSpan Delay(int attempt)
        => TimeSpan.FromMinutes(DelayMinutes[Math.Clamp(attempt, 1, DelayMinutes.Length) - 1]);

    /// <summary>Bu deneme sayısıyla kayıt kalıcı hataya düşer mi.</summary>
    public static bool Exhausted(int attempt) => attempt >= MaxAttempts;
}
