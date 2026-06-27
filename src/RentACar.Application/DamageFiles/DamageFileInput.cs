namespace RentACar.Application.DamageFiles;

public sealed class DamageFileInput
{
    public Guid VehicleId { get; set; }
    public Guid? RentalId { get; set; }
    public Guid? CariId { get; set; }
    public DateTimeOffset? AcilisTarihi { get; set; }
    public string? Aciklama { get; set; }
    public decimal? TahminiTutar { get; set; }
}
