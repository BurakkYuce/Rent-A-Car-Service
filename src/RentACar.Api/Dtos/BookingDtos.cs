using RentACar.Application.Bookings;
using RentACar.Domain.Common;
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
    // FAZ 4.5 — OTA bileşen fiyatları (opsiyonel; kanal/istemci doldurur)
    public decimal? OtaKiraBedeli { get; set; }
    public decimal? OtaDropBedeli { get; set; }
    public decimal? OtaBebekKoltugu { get; set; }
    public decimal? OtaNavigasyon { get; set; }
    public decimal? OtaLcf { get; set; }
    public decimal? OtaCdw { get; set; }
    public decimal? OtaScdw { get; set; }
    public decimal? OtaEkSurucu { get; set; }

    public BookingInput ToInput() => new()
    {
        MusteriId = MusteriId, VehicleId = VehicleId, BasTar = BasTar, BitTar = BitTar,
        GunlukUcret = GunlukUcret, CikisOfisi = CikisOfisi, DonusOfisi = DonusOfisi,
        KmLimit = KmLimit, FazlaKmUcret = FazlaKmUcret, YakitBirimUcret = YakitBirimUcret, Aciklama = Aciklama,
        OtaKiraBedeli = OtaKiraBedeli, OtaDropBedeli = OtaDropBedeli, OtaBebekKoltugu = OtaBebekKoltugu,
        OtaNavigasyon = OtaNavigasyon, OtaLcf = OtaLcf, OtaCdw = OtaCdw, OtaScdw = OtaScdw, OtaEkSurucu = OtaEkSurucu
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

/// <summary>
/// Kira yanıtı. <b>Yakıt:</b> <c>CikisYakit</c>/<c>DonusYakit</c> harici sözleşmede YÜZDE (0–100) döner — iç ölçek
/// 0–12'dir (Karar (3)), yanıtta en yakın yüzdeye çevrilir (6 → 50, 10 → 83). <c>EksikYakit</c> ise BEDELİN
/// miktarıdır ve iç birimde (on ikide bir depo) kalır: <c>YakitBedeli = EksikYakit × YakitBirimUcret</c>;
/// <c>YakitBirimUcret</c> "on ikide bir depo başına" ücrettir.
/// </summary>
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
        c.KmLimit, c.FazlaKmUcret, c.YakitBirimUcret, c.CikisKm, c.DonusKm,
        YakitOlcegi.OnIkidenYuzdeye(c.CikisYakit), YakitOlcegi.OnIkidenYuzdeye(c.DonusYakit),
        c.GercekDonusTar, c.FazlaKm, c.FazlaKmBedeli, c.EksikYakit, c.YakitBedeli, c.UzatmaGun, c.UzatmaBedeli,
        c.Aciklama, c.CreatedAtUtc, c.UpdatedAtUtc);
}

/// <summary>Araç teslim (çıkış) isteği. <c>CikisYakit</c> YÜZDE (0–100); sınırda 0–12'ye en yakına çevrilir
/// (80 → 10). Aralık dışı → 400.</summary>
public sealed class DeliverRequest
{
    public int CikisKm { get; set; }
    public int CikisYakit { get; set; }
}

/// <summary>Araç dönüş isteği. <c>DonusYakit</c> YÜZDE (0–100); sınırda 0–12'ye en yakına çevrilir. Aralık dışı → 400.</summary>
public sealed class ReturnRequest
{
    public int DonusKm { get; set; }
    public int DonusYakit { get; set; }
    public DateTimeOffset GercekDonus { get; set; }
}
