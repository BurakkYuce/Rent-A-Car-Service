using RentACar.Domain.Enums;

namespace RentACar.Application.Vehicles;

/// <summary>Araç oluştur/düzenle için giriş modeli (Blazor formu da buna bağlanır).</summary>
public sealed class VehicleInput
{
    public string Plaka { get; set; } = string.Empty;
    public string? Marka { get; set; }
    public string? Grup { get; set; }
    public string? Sube { get; set; }
    public VehicleStatus Durum { get; set; } = VehicleStatus.Stokta;
    public int Km { get; set; }
    public FuelType Yakit { get; set; } = FuelType.Benzin;
}
