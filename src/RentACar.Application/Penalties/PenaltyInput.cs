namespace RentACar.Application.Penalties;

public sealed class PenaltyInput
{
    public string CezaTuru { get; set; } = string.Empty;
    public DateTimeOffset? TebligTarihi { get; set; }
    /// <summary>Tebliğden itibaren ödeme süresi (gün). Varsayılan 15.</summary>
    public int VadeGun { get; set; } = 15;
    public Guid? VehicleId { get; set; }
    public Guid? CariId { get; set; }
    public Guid? RentalId { get; set; }
    public decimal Tutar { get; set; }
    public string? Sebep { get; set; }
}
