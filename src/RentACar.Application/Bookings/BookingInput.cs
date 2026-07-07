namespace RentACar.Application.Bookings;

/// <summary>Rezervasyon/Kira oluştur giriş modeli.</summary>
public sealed class BookingInput
{
    public Guid MusteriId { get; set; }
    public Guid? IkinciSurucuId { get; set; } // 2. sürücü (opsiyonel Customer bağı — sözleşmede gösterilir)
    public Guid VehicleId { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }
    public decimal GunlukUcret { get; set; }
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }

    // Ödeme-derinlik (roadmap A2; bilgi amaçlı, deftere yansımaz)
    public decimal? Provizyon { get; set; }
    public decimal? Depozito { get; set; }
    public decimal? KomisyonOran { get; set; }
    public decimal? KomisyonTutar { get; set; }
    public decimal? DropUcreti { get; set; }
    public decimal? SonraOdeOran { get; set; }

    public string? Aciklama { get; set; }
    public string? Kaynak { get; set; } // roadmap H2

    // TürevRent parite (additive metadata)
    public string? KiralamaTuru { get; set; }
    public string? FaturalamaTipi { get; set; }
    public string? FiyatTuru { get; set; }
    public string? Doviz { get; set; }
}
