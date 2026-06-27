using RentACar.Application.Bookings;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Api.Dtos;

/// <summary>Rezervasyon/doğrudan kira oluştur isteği. GunlukUcret 0 → fiyat motoru tarifeden çözer.</summary>
public sealed class BookingRequest
{
    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public decimal GunlukUcret { get; set; }
    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }
    public string? Aciklama { get; set; }

    public BookingInput ToInput() => new()
    {
        MusteriId = MusteriId, VehicleId = VehicleId, BasTar = BasTar, BitTar = BitTar,
        GunlukUcret = GunlukUcret, CikisOfisi = CikisOfisi, DonusOfisi = DonusOfisi,
        KmLimit = KmLimit, FazlaKmUcret = FazlaKmUcret, YakitBirimUcret = YakitBirimUcret, Aciklama = Aciklama
    };
}

public sealed record ReservationResponse(
    Guid Id, string ReservationNo, ReservationStatus Durum, Guid MusteriId, Guid VehicleId,
    DateTimeOffset BasTar, DateTimeOffset BitTar, string? CikisOfisi, string? DonusOfisi,
    int Gun, decimal GunlukUcret, decimal Tutar, int KmLimit, decimal FazlaKmUcret, decimal YakitBirimUcret,
    string? Aciklama, Guid? RentalContractId, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc)
{
    public static ReservationResponse From(Reservation r) => new(
        r.Id, r.ReservationNo, r.Durum, r.MusteriId, r.VehicleId, r.BasTar, r.BitTar, r.CikisOfisi, r.DonusOfisi,
        r.Gun, r.GunlukUcret, r.Tutar, r.KmLimit, r.FazlaKmUcret, r.YakitBirimUcret, r.Aciklama, r.RentalContractId,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

public sealed record RentalResponse(
    Guid Id, string SozlesmeNo, RentalStatus Durum, Guid? ReservationId, Guid MusteriId, Guid VehicleId,
    DateTimeOffset BasTar, DateTimeOffset BitTar, string? CikisOfisi, string? DonusOfisi,
    int Gun, decimal GunlukUcret, decimal Tutar, decimal GenelToplam, decimal Tahsilat, decimal Bakiye,
    int KmLimit, decimal FazlaKmUcret, decimal YakitBirimUcret,
    int? CikisKm, int? DonusKm, int? CikisYakit, int? DonusYakit, DateTimeOffset? GercekDonusTar,
    int FazlaKm, decimal FazlaKmBedeli, int EksikYakit, decimal YakitBedeli, int UzatmaGun, decimal UzatmaBedeli,
    string? Aciklama, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc)
{
    public static RentalResponse From(RentalContract c) => new(
        c.Id, c.SozlesmeNo, c.Durum, c.ReservationId, c.MusteriId, c.VehicleId, c.BasTar, c.BitTar,
        c.CikisOfisi, c.DonusOfisi, c.Gun, c.GunlukUcret, c.Tutar, c.GenelToplam, c.Tahsilat, c.Bakiye,
        c.KmLimit, c.FazlaKmUcret, c.YakitBirimUcret, c.CikisKm, c.DonusKm, c.CikisYakit, c.DonusYakit,
        c.GercekDonusTar, c.FazlaKm, c.FazlaKmBedeli, c.EksikYakit, c.YakitBedeli, c.UzatmaGun, c.UzatmaBedeli,
        c.Aciklama, c.CreatedAtUtc, c.UpdatedAtUtc);
}

/// <summary>Araç teslim (çıkış) isteği.</summary>
public sealed class DeliverRequest
{
    public int CikisKm { get; set; }
    public int CikisYakit { get; set; }
}

/// <summary>Araç dönüş isteği.</summary>
public sealed class ReturnRequest
{
    public int DonusKm { get; set; }
    public int DonusYakit { get; set; }
    public DateTimeOffset GercekDonus { get; set; }
}
