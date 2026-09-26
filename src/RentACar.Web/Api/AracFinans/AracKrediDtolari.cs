using RentACar.Application.AracKredileri;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.AracFinans;

/// <summary><c>POST /arac-kredileri</c>. <c>faizOran</c> kesir (0,20 = yıllık %20 basit faiz). <c>kur</c>: TRY'de boş
/// ya da 1; dövizde boş → otomatik (firma sabit kuru → TCMB). Kredi DEFTERE YAZMAZ; para yalnız taksit ödemesiyle.</summary>
public sealed record AracKrediIstegi
{
    public string? BankaAdi { get; init; }
    public Guid? VehicleId { get; init; }
    public Guid? CariId { get; init; }
    public string? DosyaNo { get; init; }
    public decimal KrediTutari { get; init; }
    public decimal FaizOran { get; init; }
    public int TaksitSayisi { get; init; }
    public DateTimeOffset? BaslangicTarihi { get; init; }
    public string? Doviz { get; init; }
    public decimal? Kur { get; init; }
    public string? Aciklama { get; init; }
}

/// <summary><c>POST /arac-kredileri/{id}/taksit-ode</c>. <c>sira</c> = ödenmek istenen taksit (ekrandaki "sonraki taksit");
/// kredide bu arada başka ödeme olduysa 409 <c>cakisma</c> (ikinci taksit sessizce ödenmez). <c>hesap</c>: "Kasa" | "Banka".</summary>
public sealed record TaksitOdeIstegi(int Sira, string? Hesap, Guid? HesapId = null, DateTimeOffset? OdemeTarihi = null);

/// <summary><c>POST /arac-kredileri/toplu-iptal</c> — yalnız Aktif krediler iptal edilir; ödenmiş taksitlere dokunulmaz.</summary>
public sealed record KrediTopluIptalIstegi(IReadOnlyList<Guid> Ids);

public sealed record KrediTopluIptalYaniti(int IptalEdilen);

public sealed record AracKrediOlusturYaniti(Guid Id, string No);

public sealed record AracKrediListeSatiri(
    Guid Id, string No, string BankaAdi, Guid? VehicleId, string? Plaka, Guid? CariId, string? CariAd, string? DosyaNo,
    decimal KrediTutari, decimal FaizOran, int TaksitSayisi, int OdenenTaksit, DateTimeOffset BaslangicTarihi,
    string Doviz, string Durum, decimal ToplamGeriOdeme, decimal AylikTaksit, decimal KalanBakiye,
    DateTimeOffset? SonVadeGunu);

/// <summary>Sonraki ödenecek taksit (kalan-yöntemi: son taksit farkı emer). Tümü ödendiyse ya da kredi aktif değilse null.</summary>
public sealed record SonrakiTaksit(int Sira, DateTimeOffset Vade, decimal Tutar);

public sealed record AracKrediYetkileri(bool TaksitOde, bool Iptal);

public sealed record AracKrediDetayYaniti(
    Guid Id, string No, string BankaAdi, Guid? VehicleId, string? Plaka, Guid? CariId, string? CariAd, string? DosyaNo,
    decimal KrediTutari, decimal FaizOran, int TaksitSayisi, int OdenenTaksit, DateTimeOffset BaslangicTarihi,
    string Doviz, decimal Kur, string Durum, string? Aciklama, AracKrediOzet Ozet, SonrakiTaksit? SonrakiTaksit,
    AracKrediYetkileri Yetkiler)
{
    public static AracKrediDetayYaniti From(AracKredi k, string? plaka, string? cariAd, AracKrediYetkileri y)
    {
        var oz = VehicleLoanService.Calculate(k);
        var sonraki = k.Durum == Domain.Enums.LoanStatus.Aktif && k.OdenenTaksit < k.TaksitSayisi && oz.Taksitler.Count > 0
            ? oz.Taksitler[k.OdenenTaksit] is var t ? new SonrakiTaksit(t.Sira, t.Vade, t.Tutar) : null
            : null;
        return new(k.Id, k.No, k.BankaAdi, k.VehicleId, plaka, k.CariId, cariAd, k.DosyaNo, k.KrediTutari, k.FaizOran,
            k.TaksitSayisi, k.OdenenTaksit, k.BaslangicTarihi, k.Currency, k.Kur, k.Durum.ToString(), k.Aciklama, oz,
            sonraki, y with { TaksitOde = y.TaksitOde && sonraki is not null });
    }
}

/// <summary>Taksit ödemesi sonucu: yazılan gider belgesi + güncel kredi.</summary>
public sealed record TaksitOdeYaniti(Guid GiderId, string GiderNo, int Sira, decimal Tutar, string Doviz,
    AracKrediDetayYaniti Kredi);
