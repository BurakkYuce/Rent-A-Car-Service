namespace RentACar.Application.VehicleGroups;

/// <summary>Araç grubu tanımı + fiyat-kural oluştur/güncelle giriş modeli. Kural alanları opsiyonel.</summary>
public sealed class VehicleGroupInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    public string? Sipp { get; set; }
    public string? Segment { get; set; }
    public string? KasaTuru { get; set; }
    public string? Marka { get; set; }
    public string? Tipi { get; set; }
    public int? KoltukSayisi { get; set; }
    public int? KapiSayisi { get; set; }
    public int? BagajSayisi { get; set; }
    public int? KucukBagaj { get; set; }
    public int? BuyukBagaj { get; set; }

    public int? SurucuMinYas { get; set; }
    public int? GencSurucuYas { get; set; }
    public int? EhliyetMinYil { get; set; }
    public int? GencEhliyetMinYil { get; set; }
    public decimal? Provizyon { get; set; }
    public decimal? Provizyon2 { get; set; }
    public decimal? MuafiyetTutari { get; set; }
    public decimal? Muafiyet2 { get; set; }
    public int? GunlukKmLimiti { get; set; }
    public int? AylikMaxKm { get; set; }
    public decimal? AsimKmUcreti { get; set; }
    public decimal? YakitFiyati { get; set; }
    public decimal? SonraOdeOran { get; set; }
    public bool? KrediKartiSart { get; set; }

    public int? WebSira { get; set; }
    public int? UpgradeSira { get; set; }

    public bool Aktif { get; set; } = true;
}
