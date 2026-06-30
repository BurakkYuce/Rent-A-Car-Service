namespace RentACar.Application.VehicleTypes;

/// <summary>Araç tip oluştur/güncelle giriş modeli.</summary>
public sealed class VehicleTypeInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Marka { get; set; }
    public string? Vites { get; set; }
    public string? Yakit { get; set; }
    public string? Grup { get; set; }
    public bool Aktif { get; set; } = true;
}
