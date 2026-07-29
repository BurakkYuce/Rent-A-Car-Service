namespace RentACar.Application.Common;

/// <summary>
/// Tenant-kapsamlı master/referans veri cache'i (dropdown kaynakları — nadiren değişir). Anahtarlar tenant'a göre
/// izole (başka tenant'ın verisi sızmaz); yazımda <see cref="Invalidate"/> ile temizlenir (bayat veri yok). TTL
/// güvenlik ağıdır (bir invalidate kaçarsa en geç TTL'de tazelenir). PII cache'lenmez (yalnız PII'siz projeksiyon).
/// </summary>
public interface ITenantCache
{
    /// <summary>Anahtar cache'te varsa döner; yoksa factory ile üretir, cache'ler, döner.
    /// <paramref name="ttl"/> verilmezse varsayılan TTL kullanılır. PR-10: anahtar-bazlı TTL, halka
    /// açık siteden okunan girdiler için gerekli — <c>Web</c> ve <c>PublicSite</c> AYRI PROCESS
    /// olduğu için <see cref="Invalidate"/> süreç sınırını geçemez, tazelik yalnız TTL'den gelir.
    /// Varsayılan sabiti düşürmek TÜM ERP cache'ini sıklaştıracağı için yalnız ilgili anahtar opt-in eder.</summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct = default, TimeSpan? ttl = null);

    /// <summary>Bu tenant'ın verilen anahtarını cache'ten düşürür (yazımda çağrılır).</summary>
    void Invalidate(string key);
}
