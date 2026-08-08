namespace RentACar.Application.VehicleSales;

public sealed class VehicleSaleInput
{
    public Guid VehicleId { get; set; }
    public Guid AliciCariId { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? NoterNo { get; set; }
    public decimal SatisNet { get; set; }
    /// <summary>KDV oranı (örn. 0.20). Araç satışı KDV'lidir.</summary>
    public decimal KdvOrani { get; set; } = 0.20m;
    public string Doviz { get; set; } = "TRY";
    /// <summary>Boş → otomatik çözüm (TRY=1; döviz KurService). Açık değer aynen kullanılır (1.1).</summary>
    public decimal? Kur { get; set; }
    public string? Aciklama { get; set; }

    // roadmap G2 (additive — bilgilendirme; deftere yansımaz)
    public decimal? HedefFiyat { get; set; }
    public int? SatisKm { get; set; }
    public string? SatisKanali { get; set; }
    public string? Devir { get; set; }

    // ---- FAZ-28: ihale/noter bilgileri (bilgi; satış tutarına etkisi yok) ----
    public DateTimeOffset? IhaleTarihi { get; set; }
    public string? IhaleFirmasi { get; set; }
    public DateTimeOffset? NoterSatisTarihi { get; set; }
}
