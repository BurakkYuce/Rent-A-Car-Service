namespace RentACar.Application.VehicleOwners;

/// <summary>Araç sahip oluştur/güncelle giriş modeli.</summary>
public sealed class VehicleOwnerInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Tur { get; set; }
    public bool Aktif { get; set; } = true;
}
