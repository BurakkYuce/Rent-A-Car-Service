using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ceza türü tanımı (master sözlük): trafik/park/HGS ihlali gibi ceza türlerinin adlandırılmış
/// listesi. Tenant-owned + auditable. Ceza kayıt formundaki serbest-metin "Ceza Türü" alanını
/// açılır listeden besler. Penalty.CezaTuru kolonu STRING kalır (FK YOK — additive).
/// VarsayilanTutar opsiyoneldir (girilirse form için öneri; ceza tutarı yine de serbest girilir).
/// </summary>
public class PenaltyType : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>Varsayılan ceza tutarı (opsiyonel öneri; ≥ 0).</summary>
    public decimal? VarsayilanTutar { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
