namespace RentACar.Application.VehicleGroups;

/// <summary>Araç grubu tanımı oluştur/güncelle giriş modeli.</summary>
public sealed class VehicleGroupInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}
