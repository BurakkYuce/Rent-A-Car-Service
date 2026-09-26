using RentACar.Application.FiloPlan;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.AracFinans;

/// <summary><c>POST /baflar</c>. <c>kullanimAmaci</c> enum ADI. Yakıt 0–12 (tek iç ölçek, Karar (3)).</summary>
public sealed record BafIstegi
{
    public Guid PersonelId { get; init; }
    public Guid VehicleId { get; init; }
    public DateTimeOffset? CikisTarihi { get; init; }
    public int CikisKm { get; init; }
    public int? CikisYakit { get; init; }
    public string? Sube { get; init; }
    public string? Aciklama { get; init; }
    public string? KullanimAmaci { get; init; }
    public Guid? Onaylayan { get; init; }
    public bool KirayaVer { get; init; }
    public TimeOnly? CikisSaat { get; init; }
}

public sealed record BafTeslimIstegi(int DonusKm, int? DonusYakit = null, DateTimeOffset? DonusTarihi = null,
    string? DonusSube = null, TimeOnly? DonusSaat = null);

public sealed record BafDto(
    Guid Id, string No, string Durum, Guid PersonelId, string PersonelAd, Guid VehicleId, string Plaka,
    DateTimeOffset CikisTarihi, TimeOnly? CikisSaat, int CikisKm, int? CikisYakit, string? Sube,
    DateTimeOffset? DonusTarihi, TimeOnly? DonusSaat, int? DonusKm, int? DonusYakit, string? DonusSube,
    string? KullanimAmaci, Guid? Onaylayan, string? OnaylayanAd, bool KirayaVer, string? Aciklama)
{
    public static BafDto From(Baf b, string plaka, string personel, string? onaylayan) => new(
        b.Id, b.No, b.Durum.ToString(), b.PersonelId, personel, b.VehicleId, plaka, b.CikisTarihi, b.CikisSaat, b.CikisKm,
        b.CikisYakit, b.Sube, b.DonusTarihi, b.DonusSaat, b.DonusKm, b.DonusYakit, b.DonusSube, b.KullanimAmaci?.ToString(),
        b.Onaylayan, onaylayan, b.KirayaVer, b.Aciklama);
}

/// <summary><c>POST /hasar-dosyalari</c>. Tahmini tutar BİLGİ (deftere yazmaz).</summary>
public sealed record HasarIstegi
{
    public Guid VehicleId { get; init; }
    public Guid? RentalId { get; init; }
    public Guid? CariId { get; init; }
    public DateTimeOffset? AcilisTarihi { get; init; }
    public string? Aciklama { get; init; }
    public decimal? TahminiTutar { get; init; }
}

public sealed record HasarNotIstegi(string? Not = null);

public sealed record HasarYetkileri(bool OnayaGonder, bool Onayla, bool Reddet, bool Kapat);

public sealed record HasarDto(
    Guid Id, string No, string Durum, Guid VehicleId, string Plaka, Guid? RentalId, Guid? CariId, string? CariAd,
    DateTimeOffset AcilisTarihi, string? Aciklama, decimal? TahminiTutar, string? OnayNotu, HasarYetkileri Yetkiler)
{
    public static HasarDto From(DamageFile f, string plaka, string? cariAd) => new(
        f.Id, f.No, f.Durum.ToString(), f.VehicleId, plaka, f.RentalId, f.CariId, cariAd, f.AcilisTarihi, f.Aciklama,
        f.TahminiTutar, f.OnayNotu, new HasarYetkileri(
            f.Durum == Domain.Enums.DamageStatus.Acik, f.Durum == Domain.Enums.DamageStatus.Onayda,
            f.Durum == Domain.Enums.DamageStatus.Onayda,
            f.Durum is Domain.Enums.DamageStatus.Onaylandi or Domain.Enums.DamageStatus.Reddedildi));
}

/// <summary><c>POST /filo-plan</c> ve <c>PUT /filo-plan/{id}</c> (PUT'ta <c>surum</c> zorunlu).</summary>
public sealed record FiloPlanIstegi(string? AracGrupAdi, string? Sipp, string? Donem, int HedefAdet,
    string? Aciklama = null, string? Surum = null);

/// <summary><c>POST /filo-plan/{id}/delta</c>: <c>yon</c> "artir" | "azalt" (±1; serbest delta YOK — Blazor kuralı).</summary>
public sealed record FiloPlanDeltaIstegi(string? Yon);

public sealed record FiloPlanDto(
    Guid Id, string? AracGrupAdi, string? Sipp, string? Donem, int HedefAdet, string? Aciklama, int Gerceklesen,
    int ToplamKayitli, int Fark, string Durum, string? Surum)
{
    public static FiloPlanDto From(FiloPlanSatir s, string? surum = null) => new(
        s.Hedef.Id, s.Hedef.AracGrupAdi, s.Hedef.Sipp, s.Hedef.Donem, s.Hedef.HedefAdet, s.Hedef.Aciklama, s.Gerceklesen,
        s.ToplamKayitli, s.Fark, s.Durum, surum);
}
