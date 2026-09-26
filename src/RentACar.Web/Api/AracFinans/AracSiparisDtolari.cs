using RentACar.Application.AracSiparisleri;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.AracFinans;

/// <summary><c>POST /arac-siparisleri</c> ve <c>PUT /arac-siparisleri/{id}</c> (PUT'ta <c>surum</c> zorunlu). Sipariş
/// DEFTERE YAZMAZ; resmi toplam Adet × BirimFiyat, Piyasa/Ops/Filo fiyatları salt bilgi.</summary>
public sealed record AracSiparisIstegi
{
    public string? Tedarikci { get; init; }
    public Guid? TedarikciCariId { get; init; }
    public DateTimeOffset? SiparisTarihi { get; init; }
    public DateTimeOffset? BeklenenTeslim { get; init; }
    public DateTimeOffset? ImzaTarih { get; init; }
    public string? DosyaNo { get; init; }
    public string? SatisTemsilci { get; init; }
    public string? OzelTemsilci { get; init; }
    public string? Marka { get; init; }
    public string? Tip { get; init; }
    public string? Grup { get; init; }
    public string? Versiyon { get; init; }
    public string? Opsiyon { get; init; }
    public string? Renk { get; init; }
    public string? IcRenk { get; init; }
    public string? KaynakTip { get; init; }
    public string? SatisTipi { get; init; }
    public string? TsbKayitNo { get; init; }
    public Guid? KrediId { get; init; }
    public int? Adet { get; init; }
    public decimal BirimFiyat { get; init; }
    public decimal? PiyasaFiyat { get; init; }
    public decimal? OpsFiyat { get; init; }
    public decimal? FiloFiyat { get; init; }
    public string? Doviz { get; init; }
    public decimal? Kur { get; init; }
    public string? Aciklama { get; init; }
    public string? Surum { get; init; }
}

public sealed record AracSiparisOlusturYaniti(Guid Id, string No);

public sealed record AracSiparisYetkileri(bool Duzenle, bool Onayla, bool TeslimAl, bool Iptal);

public sealed record AracSiparisDto(
    Guid Id, string No, string Durum, string? Surum, string Tedarikci, Guid? TedarikciCariId, string? TedarikciCariAd,
    DateTimeOffset SiparisTarihi, DateTimeOffset? BeklenenTeslim, DateTimeOffset? ImzaTarih, string? DosyaNo,
    string? SatisTemsilci, string? OzelTemsilci, string? Marka, string? Tip, string? Grup, string? Versiyon,
    string? Opsiyon, string? Renk, string? IcRenk, string? KaynakTip, string? SatisTipi, string? TsbKayitNo,
    Guid? KrediId, int Adet, decimal BirimFiyat, decimal Toplam, decimal? PiyasaFiyat, decimal? OpsFiyat,
    decimal? FiloFiyat, string Doviz, decimal Kur, string? Aciklama, AracSiparisYetkileri Yetkiler)
{
    public static AracSiparisDto From(AracSiparis s, string? version, string? customerName, AracSiparisYetkileri y) => new(
        s.Id, s.No, s.Durum.ToString(), version, s.Tedarikci, s.TedarikciCariId, customerName, s.SiparisTarihi, s.BeklenenTeslim,
        s.ImzaTarih, s.DosyaNo, s.SatisTemsilci, s.OzelTemsilci, s.Marka, s.Tip, s.Grup, s.Versiyon, s.Opsiyon, s.Renk,
        s.IcRenk, s.KaynakTip, s.SatisTipi, s.TsbKayitNo, s.KrediId, s.Adet, s.BirimFiyat, s.Adet * s.BirimFiyat,
        s.PiyasaFiyat, s.OpsFiyat, s.FiloFiyat, s.Currency, s.Kur, s.Aciklama, y);
}

/// <summary>Liste satırı. F6.2b: Blazor tablosunun sütunları için bilgi alanları EKLENDİ (versiyon, renk, kaynak/satış
/// tipi, piyasa/ops/filo fiyatı [bilgi — toplama girmez], imza tarihi, TSB no) ve satır düğmeleri için detayla AYNI
/// <c>yetkiler</c> (durum geçiş tablosu + OperationsWrite).</summary>
public sealed record AracSiparisSatiri(
    Guid Id, string No, string Durum, string Tedarikci, string? TedarikciCariAd, DateTimeOffset SiparisTarihi,
    DateTimeOffset? BeklenenTeslim, string? DosyaNo, string? Marka, string? Tip, string? Grup, int Adet,
    decimal BirimFiyat, decimal Toplam, string Doviz, Guid? KrediId, string? Versiyon, string? Renk, string? IcRenk,
    string? KaynakTip, string? SatisTipi, decimal? PiyasaFiyat, decimal? OpsFiyat, decimal? FiloFiyat,
    DateTimeOffset? ImzaTarih, string? TsbKayitNo, AracSiparisYetkileri Yetkiler);

internal static class VehicleOrderMapping
{
    public static AracSiparisInput Input(AracSiparisIstegi i, string currency, decimal exchangeRate) => new()
    {
        Tedarikci = i.Tedarikci, TedarikciCariId = i.TedarikciCariId is { } c && c != Guid.Empty ? c : null,
        SiparisTarihi = i.SiparisTarihi?.ToUniversalTime(), BeklenenTeslim = i.BeklenenTeslim?.ToUniversalTime(),
        ImzaTarih = i.ImzaTarih?.ToUniversalTime(), DosyaNo = i.DosyaNo, SatisTemsilci = i.SatisTemsilci,
        OzelTemsilci = i.OzelTemsilci, Marka = i.Marka, Tip = i.Tip, Grup = i.Grup, Versiyon = i.Versiyon,
        Opsiyon = i.Opsiyon, Renk = i.Renk, IcRenk = i.IcRenk, KaynakTip = i.KaynakTip, SatisTipi = i.SatisTipi,
        TsbKayitNo = i.TsbKayitNo, KrediId = i.KrediId is { } k && k != Guid.Empty ? k : null, Adet = i.Adet ?? 1,
        BirimFiyat = i.BirimFiyat, PiyasaFiyat = i.PiyasaFiyat, OpsFiyat = i.OpsFiyat, FiloFiyat = i.FiloFiyat,
        Doviz = currency, Kur = exchangeRate, Aciklama = i.Aciklama,
    };
}
