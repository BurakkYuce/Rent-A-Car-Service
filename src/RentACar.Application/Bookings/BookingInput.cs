namespace RentACar.Application.Bookings;

/// <summary>Rezervasyon/Kira oluştur giriş modeli.</summary>
public sealed class BookingInput
{
    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }
    public decimal GunlukUcret { get; set; }
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }
    public string? Aciklama { get; set; }
}
