namespace RentACar.Application.Baflar;

/// <summary>BAF (personel araç tahsis) oluşturma girişi (roadmap L5).</summary>
public sealed class BafInput
{
    public Guid PersonelId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset? CikisTarihi { get; set; }
    public int CikisKm { get; set; }
    public int? CikisYakit { get; set; }
    public string? Sube { get; set; }
    public string? Aciklama { get; set; }
}
