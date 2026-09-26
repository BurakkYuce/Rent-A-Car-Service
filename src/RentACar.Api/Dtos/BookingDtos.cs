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
    /// <summary>Eksik yakıt birim ücreti — harici sözleşmede YÜZDE PUANI başına (eski sözleşme korunur).
    /// İç birim on ikide bir depo başınadır; <see cref="ToInput"/> sınırda çevirir (× 100/12, 4 hane).</summary>
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
        KmLimit = KmLimit, FazlaKmUcret = FazlaKmUcret,
        YakitBirimUcret = FuelContract.UnitFeeInclusive(YakitBirimUcret), Aciklama = Aciklama,
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
        r.Gun, r.GunlukUcret, r.Tutar, r.KmLimit, r.FazlaKmUcret, FuelContract.UnitFeeExclusive(r.YakitBirimUcret), r.Aciklama, r.RentalContractId,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

/// <summary>
/// Kira yanıtı — harici YÜZDE sözleşmesi (Karar (3)): iç ölçek 0–12, yanıtta çevrilir.
/// <c>CikisYakit</c>/<c>DonusYakit</c>/<c>EksikYakit</c> yüzde puanı (6/12 → 50); <c>YakitBirimUcret</c> yüzde puanı
/// başına (2 hane). Böylece eski sözleşmenin <c>EksikYakit × YakitBirimUcret ≈ YakitBedeli</c> ilişkisi korunur.
/// <para><b>Hassasiyet:</b> her okuma iç ölçekte en yakın on ikide bire yuvarlanır (±1/12 depo ≈ ±4 yüzde puanı);
/// geri çeviri kayıplıdır (80 → 10 → 83). <c>YakitBedeli</c> iç birimle hesaplanan GERÇEK tutardır; yüzde
/// alanlarından yeniden hesaplanan değer kuruş düzeyinde farklı olabilir.</para>
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
        c.KmLimit, c.FazlaKmUcret, FuelContract.UnitFeeExclusive(c.YakitBirimUcret), c.CikisKm, c.DonusKm,
        FuelScale.TwelfthsToPercent(c.CikisYakit), FuelScale.TwelfthsToPercent(c.DonusYakit),
        c.GercekDonusTar, c.FazlaKm, c.FazlaKmBedeli, FuelScale.TwelfthsToPercent(c.EksikYakit), c.YakitBedeli, c.UzatmaGun, c.UzatmaBedeli,
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

/// <summary>
/// Harici API yakıt birim ücreti çevirisi (adversarial HIGH-1). Eski sözleşmede birim ücret YÜZDE PUANI başınaydı;
/// iç birim on ikide bir depo başına. Çevrilmezse yüzde başı fiyat on ikide bir düşüşle çarpılıp ~8,33 kat eksik
/// faturalanırdı (%50→%25, birim 100: eski 2500, çevirisiz 300). İçeri: × 100/12, <c>numeric(19,4)</c> kolonuna
/// 4 haneye yuvarlanır (10 → 83,3333); bedel satırı ReturnMath'te 2 haneye yuvarlandığı için 6 × 83,3333 = 499,9998
/// → 500,00. Dışarı: × 12/100, 2 hane (83,3333 → 10,00).
/// </summary>
public static class FuelContract
{
    /// <summary>Üst sınır: çevrilmiş değer <c>numeric(19,4)</c>'e sığsın, decimal taşması 500 olmasın.</summary>
    public const decimal MaxUnitFee = 1_000_000_000m;

    public static decimal UnitFeeInclusive(decimal perPercent)
        => perPercent > MaxUnitFee
            ? throw new RentACar.Application.Common.ValidationException(
                "Yakıt birim ücreti çok büyük.", "yakitBirimUcret")
            : Math.Round(perPercent * FuelScale.MaxPercent / FuelScale.Max, 4, MidpointRounding.AwayFromZero);

    public static decimal UnitFeeExclusive(decimal perTwelfth)
        => Math.Round(perTwelfth * FuelScale.Max / FuelScale.MaxPercent, 2, MidpointRounding.AwayFromZero);
}
