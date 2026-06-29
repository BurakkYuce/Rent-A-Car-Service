using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç sipariş/tedarik kaydı (roadmap L3): tedarikçiden araç siparişi (FiloStatus.Siparis akışı).
/// Tenant-owned + auditable; full-CRUD (mali değişmez belge DEĞİL). DEFTER POSTLAMAZ — teslim alınınca
/// araç/satınalma faturalama ayrı; bu kayıt sipariş takibinin kaynağıdır.
/// </summary>
public class AracSiparis : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (SP-000001).</summary>
    public string No { get; set; } = string.Empty;

    public string Tedarikci { get; set; } = string.Empty;
    public DateTimeOffset SiparisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? BeklenenTeslim { get; set; }

    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Grup { get; set; }

    public int Adet { get; set; } = 1;
    public decimal BirimFiyat { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public SiparisDurum Durum { get; set; } = SiparisDurum.Bekliyor;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
