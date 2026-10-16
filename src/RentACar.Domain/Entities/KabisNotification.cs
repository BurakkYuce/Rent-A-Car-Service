using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kira başına KABİS bildirim kaydı (tür başına tek satır): teslimde <see cref="KabisNoticeType.Kiralama"/>,
/// dönüşte <see cref="KabisNoticeType.Iade"/>, teslim edilmiş kiranın iptalinde <see cref="KabisNoticeType.Iptal"/>.
///
/// <para><b>İdempotency ŞEMADA:</b> <c>(TenantId, KiraId, Tur)</c> benzersizdir. Aynı olay iki kez tetiklense
/// (çift tık, iki replika, job tekrarı) ikinci satır yazılamaz; gönderim yalnız bu satırın durumu
/// <see cref="KabisNoticeStatus.Bekliyor"/> iken yapılır.</para>
///
/// <para><b>PII YOK:</b> bu tabloda TC kimlik, pasaport ya da ehliyet numarası TUTULMAZ. Gönderim yükü her
/// denemede kaynak kayıtlardan (şifreli cari alanları) BELLEKTE kurulur ve kalıcılaşmaz. <see cref="SonHata"/>
/// yazılmadan önce yükteki kimlik değerleri ve uzun rakam dizileri maskelenir.</para>
///
/// <para><b>IAuditable DEĞİL</b> (<see cref="GidenMesaj"/> kararıyla aynı): bu bir işlem kaydıdır; her deneme
/// denetim izine kopyalanırsa iz gürültüye boğulur. Operatör müdahalesi (gerekli değil / elle bildirildi)
/// kim-ne zaman-neden alanlarıyla satırın kendisinde tutulur.</para>
/// </summary>
public class KabisNotification : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>İlgili kira sözleşmesi (FK → Rentals).</summary>
    public Guid KiraId { get; set; }

    public KabisNoticeType Tur { get; set; }
    public KabisNoticeStatus Durum { get; set; } = KabisNoticeStatus.Bekliyor;

    /// <summary>Kaydın oluştuğu andaki kurum ayarı (Emniyet/Jandarma) — sonradan ayar değişse de iz kalır.</summary>
    public KabisAuthority Kurum { get; set; }

    /// <summary>Olayın gerçek anı: teslim anı (Kiralama), gerçek dönüş (Iade), iptal anı (Iptal).</summary>
    public DateTimeOffset OlayUtc { get; set; }

    /// <summary>Karşı sisteme yapılan (bağlantı kurulmuş) deneme sayısı. "Bağlantı yapılandırılmadı" denemesi SAYILMAZ.</summary>
    public int DenemeSayisi { get; set; }

    /// <summary>Son hatanın operatöre gösterilecek, PII'sı maskelenmiş hâli (en fazla 512 karakter).</summary>
    public string? SonHata { get; set; }

    /// <summary>Karşı sistemin verdiği bildirim referansı (başarıda dolu). Elle bildirimde operatörün girdiği referans.</summary>
    public string? DisReferans { get; set; }

    /// <summary>true: operatör portaldan elle bildirdiğini kayda geçirdi (sistem göndermedi).</summary>
    public bool Elle { get; set; }

    /// <summary>"Gerekli değil" / "elle bildirildi" gerekçesi ya da sistemin iptal notu.</summary>
    public string? Gerekce { get; set; }

    /// <summary>Elle müdahaleyi yapan kullanıcı (sistem geçişlerinde null).</summary>
    public Guid? IsaretleyenKullaniciId { get; set; }

    public DateTimeOffset OlusturmaUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SonDenemeUtc { get; set; }

    /// <summary>Yeniden deneme işinin bu satırı en erken alacağı an (geri çekilme). Null → hemen.</summary>
    public DateTimeOffset? SonrakiDenemeUtc { get; set; }

    /// <summary>
    /// Gönderim sahiplenmesinin bitişi (kısa kira): doluysa ve gelecekteyse bir gönderici şu an bu satırı gönderiyor;
    /// başka bir koşu/istek alamaz. Sonuç yazılınca temizlenir; süreç çökerse süre dolunca satır yeniden alınır.
    /// </summary>
    public DateTimeOffset? SahiplenmeBitisUtc { get; set; }

    /// <summary>Bildirildi'ye geçiş anı.</summary>
    public DateTimeOffset? BildirimUtc { get; set; }
}
