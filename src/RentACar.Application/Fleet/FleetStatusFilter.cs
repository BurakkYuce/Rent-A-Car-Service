using RentACar.Domain.Enums;

namespace RentACar.Application.Fleet;

/// <summary>Araç Güncel Durum gridi filtreleri. Sube, servis tarafından şube kapsamına ayarlanır.</summary>
public sealed class FleetStatusFilter
{
    /// <summary>Plaka/marka içeren arama (case-insensitive).</summary>
    public string? Query { get; set; }
    public VehicleStatus? Durum { get; set; }
    public FiloStatus? FiloDurum { get; set; }
    public string? Grup { get; set; }
    public string? Marka { get; set; }
    public Vites? Vites { get; set; }
    public FuelType? Yakit { get; set; }
    public string? Sube { get; set; }
    /// <summary>true → yalnız kirada; false → yalnız kirada olmayan; null → tümü.</summary>
    public bool? KiradaMi { get; set; }
}
