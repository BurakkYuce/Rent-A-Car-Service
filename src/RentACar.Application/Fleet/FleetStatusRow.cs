using RentACar.Domain.Enums;

namespace RentACar.Application.Fleet;

/// <summary>
/// Araç Güncel Durum gridi satırı: araç kimlik/filo bilgisi + (varsa) aktif kira özeti.
/// Salt-okunur projeksiyon — araç + aktif RentalContract + müşteri birleştirilir.
/// </summary>
public sealed class FleetStatusRow
{
    public Guid VehicleId { get; init; }
    public string Plaka { get; init; } = string.Empty;
    public string? Marka { get; init; }
    public string? Tip { get; init; }
    public string? Grup { get; init; }
    public string? Segment { get; init; }
    public string? Sipp { get; init; }
    public Vites? Vites { get; init; }
    public FuelType Yakit { get; init; }
    public int Km { get; init; }
    public string? Sube { get; init; }

    /// <summary>Operasyonel durum (Boş/Kirada/Serviste…).</summary>
    public VehicleStatus Durum { get; init; }
    /// <summary>Filo yaşam döngüsü statüsü (stok/havuz/tahsis…).</summary>
    public FiloStatus? FiloDurum { get; init; }

    // Aktif kira (yoksa null)
    public Guid? AktifKiraId { get; init; }
    public string? KiraSozlesmeNo { get; init; }
    public string? MusteriAd { get; init; }
    public DateTimeOffset? KiraBitTar { get; init; }
    public decimal? KiraBakiye { get; init; }

    public bool Kirada => AktifKiraId is not null;
}
