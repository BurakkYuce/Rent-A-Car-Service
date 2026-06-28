namespace RentACar.Application.VehicleColors;

/// <summary>Renk oluştur/güncelle giriş modeli.</summary>
public sealed class VehicleColorInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
