using RentACar.Domain.Enums;

namespace RentACar.Application.Vehicles;

/// <summary>Araç liste arama/filtre + sayfalama. Sube servis tarafından şube kapsamına ayarlanır.</summary>
public sealed class VehicleFilter
{
    public string? Query { get; set; }       // plaka/marka (içeren, case-insensitive)
    public VehicleStatus? Durum { get; set; }
    public string? Grup { get; set; }
    public string? Sube { get; set; }         // rol bazlı şube kapsamı (servis ayarlar)
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
