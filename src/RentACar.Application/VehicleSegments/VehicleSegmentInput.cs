namespace RentACar.Application.VehicleSegments;

/// <summary>Araç segment oluştur/güncelle giriş modeli.</summary>
public sealed class VehicleSegmentInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}
