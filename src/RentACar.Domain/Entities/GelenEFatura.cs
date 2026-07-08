using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Gelen (satın-alma) e-Fatura kaydı — GİB'den çekilen veya elle girilen gelen faturaların triage
/// kutusu (canlı TürevRent gelen_e_fatura_listesi karşılığı). ETTN tenant içinde benzersiz.
/// Triage iş akışı: Beklemede → Onaylandı/Reddedildi; Onaylandı → İşlendi. Tenant-owned + auditable.
/// NOT: Bu kayıt DEFTERE POSTLAMAZ — gelen faturayı gidere/borca dönüştürme (para hareketi) ileriki
/// bir adımdır (adversarial gerektirir). GİB'den gerçek çekiş kimlik-bağımlı (IEInvoiceService stub).
/// </summary>
public class GelenEFatura : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>ETTN (e-fatura tekil no) — tenant içinde benzersiz.</summary>
    public string Ettn { get; set; } = string.Empty;
    public string GonderenVkn { get; set; } = string.Empty;
    public string GonderenUnvan { get; set; } = string.Empty;
    public DateTimeOffset Tarih { get; set; }

    public decimal NetTutar { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string Currency { get; set; } = "TRY";

    public GelenEFaturaDurum Durum { get; set; } = GelenEFaturaDurum.Beklemede;
    public string? RedNedeni { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
