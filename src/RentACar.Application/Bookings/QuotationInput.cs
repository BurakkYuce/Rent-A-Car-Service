namespace RentACar.Application.Bookings;

/// <summary>Teklif oluştur/güncelle giriş modeli (rezervasyon alanları + geçerlilik).</summary>
public sealed class QuotationInput
{
    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }
    public decimal GunlukUcret { get; set; }
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }
    public DateTimeOffset? GecerlilikTarihi { get; set; }
    public string? Aciklama { get; set; }

    /// <summary>Doğrulama/hesap için rezervasyon giriş modeline köprü.</summary>
    public BookingInput ToBooking() => new()
    {
        MusteriId = MusteriId,
        VehicleId = VehicleId,
        BasTar = BasTar,
        BitTar = BitTar,
        CikisOfisi = CikisOfisi,
        DonusOfisi = DonusOfisi,
        GunlukUcret = GunlukUcret,
        KmLimit = KmLimit,
        FazlaKmUcret = FazlaKmUcret,
        YakitBirimUcret = YakitBirimUcret,
        Aciklama = Aciklama
    };
}
