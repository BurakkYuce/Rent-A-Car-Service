using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Arka plan (otomatik servis) koşu günlüğü — canlı TürevRent <c>otomatik_servisler.aspx</c>
/// karşılığı. Her tenant-kapsamlı üretici koşusu için TEK satır: ne zaman başladı, ne kadar sürdü,
/// başarılı mıydı, kaç kayıt üretti.
///
/// <para><b>Neden var:</b> üreticiler (vade bildirimi, filo bildirimi, dönem faturalama) sessizce
/// çalışıyordu; kullanıcı bir işin gerçekten koşup koşmadığını göremiyordu. Hata durumunda tek iz
/// sunucu logunda kalıyordu — kullanıcının erişimi yok.</para>
///
/// <para><b>Kapsam yalnız TENANT-KAPSAMLI işler.</b> Platform işleri (TCMB kur çekimi, bekleyen
/// alan adı süre aşımı) hiçbir tenant'a ait değildir; tenant-owned bir tabloya yazılamazlar ve
/// bilinçli olarak kapsam dışıdır.</para>
///
/// <para><see cref="IAuditable"/> DEĞİL: satırı bir kullanıcı değil sistem yazar, ve kaydın kendi
/// zaman damgaları (<see cref="BaslangicUtc"/>/<see cref="BitisUtc"/>) zaten var — audit sütunları
/// tekrar olurdu (<c>Bildirim</c> ile aynı gerekçe). Ekleme-sonrası DEĞİŞTİRİLMEZ (append-only).</para>
/// </summary>
public class JobCalismaLog : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>İş adı — mevcut metrik etiketleriyle AYNI sözlük (<c>RacarMetrics.JobFailed</c>):
    /// "vade-bildirim", "filo-bildirim", "donem-fatura". Enum DEĞİL: yeni bir iş eklemek şema
    /// göçü gerektirmemeli, ve enum'a canlı sistemin iş adlarını yazmak bizde hiç koşmayan
    /// türler üretirdi.</summary>
    public string JobAdi { get; set; } = string.Empty;

    public DateTimeOffset BaslangicUtc { get; set; }
    public DateTimeOffset BitisUtc { get; set; }

    public bool Basarili { get; set; }

    /// <summary>İşin ürettiği kayıt sayısı (bildirim adedi, kesilen fatura adedi…). İş sayı
    /// döndürmüyorsa null.</summary>
    public int? SonucSayisi { get; set; }

    /// <summary>Başarısızsa hata mesajı; başarılıysa kısa özet (ör. atlanan tenant gerekçeleri).</summary>
    public string? Detay { get; set; }

    /// <summary>Koşu süresi (ms) — türetilmiş, saklanmaz.</summary>
    public int SureMs => (int)Math.Max(0, (BitisUtc - BaslangicUtc).TotalMilliseconds);
}
