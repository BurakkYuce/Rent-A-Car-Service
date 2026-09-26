using RentACar.Domain.Enums;

namespace RentACar.Application.Fleet;

/// <summary>Araç Güncel Durum gridi filtreleri. Sube, servis tarafından şube kapsamına ayarlanır.</summary>
public sealed class FleetStatusFilter
{
    /// <summary>Plaka/marka içeren arama (case-insensitive).</summary>
    public string? Query { get; set; }
    public VehicleStatus? Durum { get; set; }
    public FleetLifecycleStatus? FiloDurum { get; set; }
    public string? Grup { get; set; }
    public string? Marka { get; set; }
    public Transmission? Vites { get; set; }
    public FuelType? Yakit { get; set; }
    public string? Sube { get; set; }         // UI şube filtresi (kullanıcı seçimi)
    /// <summary>Rol bazlı şube KAPSAMI (C3; servis ayarlar) — UI filtresinden bağımsız zorlanır.</summary>
    public Authorization.BranchScope.BranchFilter Kapsam { get; set; }
    /// <summary>true → yalnız kirada; false → yalnız kirada olmayan; null → tümü.</summary>
    public bool? KiradaMi { get; set; }

    // ---- FAZ-11 ----
    /// <summary>Pasife alma gerekçesi (FAZ-10 alanı) — İÇEREN arama; canlıda serbest metin girilir.</summary>
    public string? PasifSebep { get; set; }
    /// <summary>HGS/OGS etiket no — içeren arama (etiketi elindeki operatör aracı bulabilsin).</summary>
    public string? HgsNo { get; set; }
    /// <summary>GPS takip cihazı no (FAZ-10 <c>TakipNo</c>) — içeren arama.</summary>
    public string? TakipNo { get; set; }
    /// <summary>true → yalnız kar lastiği takılı; false → yalnız takılı olmayan; null → tümü.</summary>
    public bool? KarLastigi { get; set; }
    /// <summary>true → yalnız web rezervasyonuna KAPALI araçlar; false → yalnız açık olanlar.</summary>
    public bool? WebRezKapat { get; set; }
    /// <summary>true → yalnız ofis rezervasyonuna KAPALI araçlar; false → yalnız açık olanlar.</summary>
    public bool? OfisRezKapat { get; set; }
}
