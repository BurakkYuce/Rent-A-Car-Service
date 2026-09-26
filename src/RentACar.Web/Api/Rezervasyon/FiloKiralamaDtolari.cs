using RentACar.Application.FiloKiralamalar;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary><c>POST /filo-kiralama</c> — Blazor filo formunun alanları (FiloKiralamaInput). KdvOrani kesir (0.20 = %20).</summary>
public sealed record FiloKiralamaIstegi
{
    public required Guid MusteriId { get; init; }
    public required Guid VehicleId { get; init; }
    public DateTimeOffset? BasTar { get; init; }
    public int? SureAy { get; init; }
    public decimal? AylikUcret { get; init; }
    public decimal? KdvOrani { get; init; }
    public string? Doviz { get; init; }
    public decimal? Kur { get; init; }
    public int? ToplamKmLimiti { get; init; }
    public decimal? DamgaVergisi { get; init; }
    public string? Aciklama { get; init; }
    public string? SatisTemsilcisi { get; init; }
    public string? FaturaTuru { get; init; }
    public DateTimeOffset? SozlesmeTarihi { get; init; }
    public DateTimeOffset? ImzaTarih { get; init; }
    public string? MakbuzNo { get; init; }
    public string? DosyaNo { get; init; }
    public string? SozlesmeNo { get; init; }
    public int? VadeGun { get; init; }
    public string? FiyatTuru { get; init; }
    public string? Kaynak { get; init; }
    public int? CikisKm { get; init; }
    public int? ToplamKm { get; init; }
}

/// <summary><c>PUT /filo-kiralama/{id}/kunye</c> — KÜNYE tam değiştirmesi; para/süre alanları tipte YOK. <c>surum</c> ZORUNLU.</summary>
public sealed record FiloKunyeIstegi
{
    public string? Surum { get; init; }
    public string? SatisTemsilcisi { get; init; }
    public string? FaturaTuru { get; init; }
    public DateTimeOffset? SozlesmeTarihi { get; init; }
    public DateTimeOffset? ImzaTarih { get; init; }
    public string? MakbuzNo { get; init; }
    public string? DosyaNo { get; init; }
    public string? SozlesmeNo { get; init; }
    public int? VadeGun { get; init; }
    public string? FiyatTuru { get; init; }
    public string? Kaynak { get; init; }
    public int? CikisKm { get; init; }
    public int? ToplamKm { get; init; }
    public int? ToplamKmLimiti { get; init; }
    public string? Aciklama { get; init; }
}

public sealed record FiloListeSatiri(
    Guid Id, string No, string? SozlesmeNo, Guid MusteriId, string MusteriAd, Guid VehicleId, string Plaka,
    DateTimeOffset BasTar, int SureAy, decimal AylikUcret, decimal GenelToplam, string Doviz, string? SatisTemsilcisi,
    string? Kaynak, int? VadeGun, string Durum);

public sealed record FiloTaksitDto(int Sira, DateTimeOffset Vade, decimal Net, decimal Kdv, decimal Toplam);

public sealed record FiloOzetDto(decimal ToplamNet, decimal ToplamKdv, decimal Damga, decimal GenelToplam, IReadOnlyList<FiloTaksitDto> Taksitler);

public sealed record FiloYetkileri(bool Kunye, bool Tamamla, bool Iptal);

public sealed record FiloKiralamaDto(
    Guid Id, string No, string Durum, string? Surum, Guid MusteriId, string MusteriAd, Guid VehicleId, string Plaka,
    DateTimeOffset BasTar, int SureAy, decimal AylikUcret, decimal KdvOrani, string Doviz, decimal Kur,
    int? ToplamKmLimiti, decimal? DamgaVergisi, string? Aciklama, string? SatisTemsilcisi, string? FaturaTuru,
    DateTimeOffset? SozlesmeTarihi, DateTimeOffset? ImzaTarih, string? MakbuzNo, string? DosyaNo, string? SozlesmeNo,
    int? VadeGun, string? FiyatTuru, string? Kaynak, int? CikisKm, int? ToplamKm, FiloOzetDto Ozet, FiloYetkileri Yetkiler)
{
    public static FiloKiralamaDto From(FiloKiralama k, string? version, string customerName, string plate, FiloYetkileri y)
    {
        var o = FleetRentalService.InstallmentPlan(k); // salt-hesap SUNUCUDA (UI formül taşımaz)
        return new(k.Id, k.No, k.Durum.ToString(), version, k.MusteriId, customerName, k.VehicleId, plate, k.BasTar, k.SureAy,
            k.AylikUcret, k.KdvOrani, k.Currency, k.Kur, k.ToplamKmLimiti, k.DamgaVergisi, k.Aciklama, k.SatisTemsilcisi,
            k.FaturaTuru, k.SozlesmeTarihi, k.ImzaTarih, k.MakbuzNo, k.DosyaNo, k.SozlesmeNo, k.VadeGun, k.FiyatTuru,
            k.Kaynak, k.CikisKm, k.ToplamKm,
            new FiloOzetDto(o.ToplamNet, o.ToplamKdv, o.Damga, o.GenelToplam,
                o.Taksitler.Select(t => new FiloTaksitDto(t.Sira, t.Vade, t.Net, t.Kdv, t.Toplam)).ToList()),
            y);
    }
}

public sealed record FiloOlusturYaniti(Guid Id, string No);
