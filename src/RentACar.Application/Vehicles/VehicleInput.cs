using RentACar.Domain.Enums;

namespace RentACar.Application.Vehicles;

/// <summary>Araç oluştur/düzenle için giriş modeli (Blazor formu da buna bağlanır).</summary>
public sealed class VehicleInput
{
    public string Plaka { get; set; } = string.Empty;
    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Grup { get; set; }
    public string? Segment { get; set; }
    public string? Sipp { get; set; }
    public string? Renk { get; set; }
    public int? ModelYili { get; set; }
    public Vites? Vites { get; set; }
    public string? SasiNo { get; set; }
    public string? MotorNo { get; set; }
    public string? Sube { get; set; }
    public VehicleStatus Durum { get; set; } = VehicleStatus.Stokta;
    public FiloStatus? FiloDurum { get; set; }
    public int Km { get; set; }
    public FuelType Yakit { get; set; } = FuelType.Benzin;

    // Parite zenginleştirme (additive, opsiyonel)
    public int? MotorGucu { get; set; }
    public int? SilindirHacmi { get; set; }
    public string? RuhsatNo { get; set; }
    public DateTimeOffset? TescilTarihi { get; set; }
    public string? AracSahibi { get; set; }
    public decimal? AlimBedeli { get; set; }
    public DateTimeOffset? AlimTarihi { get; set; }
    public decimal? AlisVergisiz { get; set; }
    public decimal? AlisOtv { get; set; }
    public decimal? AlisKdv { get; set; }
    public decimal? AylikMaliyet { get; set; }
    public decimal? FiloYonetimMaliyeti { get; set; }
    public decimal? IkinciElDeger { get; set; }
    public DateTimeOffset? FiloGirisTarih { get; set; }
    public DateTimeOffset? FiloCikisTarih { get; set; }
    public string? OzelKod1 { get; set; }
    public string? OzelKod2 { get; set; }
    public string? OzelKod3 { get; set; }
    public string? OzelKod4 { get; set; }
    public string? OzelKod5 { get; set; }

    // roadmap G1
    public string? HgsNo { get; set; }
    public string? OgsNo { get; set; }
    public string? KasaTipi { get; set; }
    public string? DetayTipi { get; set; }
    public string? AlimFaturaNo { get; set; }
    public string? AlimYapilanFirma { get; set; }
    public int? KiraKmLimiti { get; set; }

    // Operasyon bayrakları (roadmap K2)
    public bool WebRezKapat { get; set; }
    public bool OfisRezKapat { get; set; }
    public bool ZIzni { get; set; }
    public bool Utts { get; set; }
    public bool KarLastigi { get; set; }
    public bool YedekAnahtar { get; set; }
    public bool Temizlik { get; set; }
    public bool Rehin { get; set; }
    // Bakım/lastik (roadmap K2)
    public DateTimeOffset? SonBakimTarih { get; set; }
    public int? SonBakimKm { get; set; }
    public string? LastikDurumu { get; set; }
}
