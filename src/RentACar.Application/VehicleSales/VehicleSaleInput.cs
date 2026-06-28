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
    public decimal Kur { get; set; } = 1m;
    public string? Aciklama { get; set; }

    // roadmap G2 (additive — bilgilendirme; deftere yansımaz)
    public decimal? HedefFiyat { get; set; }
    public int? SatisKm { get; set; }
    public string? SatisKanali { get; set; }
    public string? Devir { get; set; }
}
