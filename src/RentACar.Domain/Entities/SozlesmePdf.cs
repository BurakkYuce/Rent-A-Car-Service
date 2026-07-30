using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// PR-C — paylaşılan sözleşmenin <b>anlık görüntüsü</b> (PDF baytı).
///
/// <para><b>Neden saklıyoruz:</b> sözleşme PDF'i DB'de tutulmuyordu, her istekte
/// <c>SozlesmeService.GetAsync</c> + <c>PdfExportService.Contract</c> ile yeniden üretiliyordu. Bir
/// linki müşteriye verdikten sonra bu davranış kabul edilemez: kira güncellenirse müşterinin elindeki
/// link sessizce BAŞKA bir belgeye dönüşür. Link sabit bir belgeyi göstermek zorunda → anlık görüntü.</para>
///
/// <para><b>Tenant-owned + RLS</b> — <see cref="PaylasimLink"/>'in tersine. Ayrım bilinçli: PDF müşteri
/// adı/adresi/TC'si taşır, yani kişisel veri; RLS'in ardında kalmalı. Anonim uç token'ı önce RLS'siz
/// <see cref="PaylasimLink"/>'ten çözer, tenant'ı öğrenir, GUC'u açar, sonra buraya erişir
/// (<c>CalendarFeedService</c>'in iki-fazlı deseni).</para>
///
/// <para>Kira başına <b>tek</b> satır (unique index). "Yeni sürüm" aynı satırın baytını değiştirir;
/// iptal satırı SİLER — aksi halde iptal edilmiş her paylaşım bir PDF'i süresiz taşırdı.</para>
///
/// <para><b><see cref="IAuditable"/> DEĞİL — bilinçli.</b> <c>AuditSaveChangesInterceptor</c> denetim
/// satırına entity'nin TÜM property'lerini JSON olarak yazar; bu tabloda <see cref="Bytes"/> olduğu
/// için her paylaşım/sürüm/iptal, PDF'in tamamını base64 olarak <c>AuditLog</c>'a kopyalardı (60 KB
/// sözleşme → ~80 KB denetim satırı). Denetim izi ait olduğu yerde: <see cref="PaylasimLink"/>
/// IAuditable'dır ve yalnız küçük skaler alanlar taşır.</para>
/// </summary>
public class SozlesmePdf : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RentalId { get; set; }

    public byte[] Bytes { get; set; } = [];
    public long Boyut { get; set; }

    /// <summary>Bu baytın üretildiği an — "bayat mı" kararı kiranın <c>UpdatedAtUtc</c>'siyle karşılaştırılarak.</summary>
    public DateTimeOffset UretimUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
