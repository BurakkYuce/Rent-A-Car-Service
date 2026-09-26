using RentACar.Domain.Entities;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// Tarife matrisi satırı. <c>onaylayan</c>/<c>onayZaman</c> are SERVER-stamped: on PUT/POST they are taken from the
/// session when <c>onayDurumu</c> becomes <c>Onayli</c> (client values ignored — an approval cannot be attributed
/// to someone else or back-dated), kept when the state does not change, cleared when it leaves <c>Onayli</c>.
/// </summary>
public sealed record RateMatrixDto(Guid Id, string Kod, string Ad, string? Aciklama, string? Kanal, string? Sube,
    string? Lokasyon, string? Turu, int? KiraSuresi, string? AracGrupKod, string? ParaBirimi, DateTimeOffset? BasTar,
    DateTimeOffset? BitTar, decimal? Gun1, decimal? Gun2, decimal? Gun3, decimal? Gun4, decimal? Gun5, decimal? Gun6,
    decimal? Gun7, decimal? GunHaftalik, decimal? GunAylik, int? Km1, int? Km2, int? Km3, int? Km4, int? Km5, int? Km6,
    decimal? Km1Ucret, decimal? Km2Ucret, decimal? Km3Ucret, decimal? Km4Ucret, decimal? Km5Ucret, decimal? Km6Ucret,
    int? KmHaftalik, decimal? KmHaftalikUcret, int? KmAylik, decimal? KmAylikUcret, decimal? MaxEsneklik,
    string OnayDurumu, string? Onaylayan, DateTimeOffset? OnayZaman, bool Aktif, string? Surum)
{
    public static RateMatrixDto From(RateMatrix x, string? version) => new(x.Id, x.Kod, x.Ad, x.Aciklama, x.Kanal, x.Sube,
        x.Lokasyon, x.Turu, x.KiraSuresi, x.AracGrupKod, x.ParaBirimi, x.BasTar, x.BitTar, x.Gun1, x.Gun2, x.Gun3, x.Gun4,
        x.Gun5, x.Gun6, x.Gun7, x.GunHaftalik, x.GunAylik, x.Km1, x.Km2, x.Km3, x.Km4, x.Km5, x.Km6, x.Km1Ucret,
        x.Km2Ucret, x.Km3Ucret, x.Km4Ucret, x.Km5Ucret, x.Km6Ucret, x.KmHaftalik, x.KmHaftalikUcret, x.KmAylik,
        x.KmAylikUcret, x.MaxEsneklik, x.OnayDurumu.ToString(), x.Onaylayan, x.OnayZaman, x.Aktif, version);
}

public sealed record RateMatrixRequest(string? Kod, string? Ad, string? Aciklama, string? Kanal, string? Sube,
    string? Lokasyon, string? Turu, int? KiraSuresi, string? AracGrupKod, string? ParaBirimi, DateTimeOffset? BasTar,
    DateTimeOffset? BitTar, decimal? Gun1, decimal? Gun2, decimal? Gun3, decimal? Gun4, decimal? Gun5, decimal? Gun6,
    decimal? Gun7, decimal? GunHaftalik, decimal? GunAylik, int? Km1, int? Km2, int? Km3, int? Km4, int? Km5, int? Km6,
    decimal? Km1Ucret, decimal? Km2Ucret, decimal? Km3Ucret, decimal? Km4Ucret, decimal? Km5Ucret, decimal? Km6Ucret,
    int? KmHaftalik, decimal? KmHaftalikUcret, int? KmAylik, decimal? KmAylikUcret, decimal? MaxEsneklik,
    string? OnayDurumu, bool Aktif = true, string? Surum = null) : ICatalogRequest;

/// <summary>Kiralama kuralı / kampanya.</summary>
public sealed record RentalRuleDto(Guid Id, string Kod, string Ad, string? Aciklama, string? Kanal, string? Sube,
    string? AracGrupKod, int? MinGun, int? MaxGun, decimal? Iskonto, decimal? HaftaSonuFarkOran, decimal? SonraOdeOran,
    int? HediyeGun, bool KampanyaMi, string? KampanyaKodu, string? MusteriSegment, DateTimeOffset? GecerlilikBas,
    DateTimeOffset? GecerlilikBit, string? SartMetni, DateTimeOffset? TalepBas, DateTimeOffset? TalepBit,
    string? PromosyonTuru, string? KuponGecerlilik, string? HesaplamaTipi, bool HizliIslem, string? HaftaGunKisiti,
    string TarihTipi, string KampanyaDurum, bool Aktif, string? Surum)
{
    public static RentalRuleDto From(RentalRule x, string? version) => new(x.Id, x.Kod, x.Ad, x.Aciklama, x.Kanal, x.Sube,
        x.AracGrupKod, x.MinGun, x.MaxGun, x.Iskonto, x.HaftaSonuFarkOran, x.SonraOdeOran, x.HediyeGun, x.KampanyaMi,
        x.KampanyaKodu, x.MusteriSegment, x.GecerlilikBas, x.GecerlilikBit, x.SartMetni, x.TalepBas, x.TalepBit,
        x.PromosyonTuru?.ToString(), x.KuponGecerlilik?.ToString(), x.HesaplamaTipi?.ToString(), x.HizliIslem,
        x.HaftaGunKisiti, x.TarihTipi.ToString(), x.KampanyaDurum.ToString(), x.Aktif, version);
}

public sealed record RentalRuleRequest(string? Kod, string? Ad, string? Aciklama, string? Kanal, string? Sube,
    string? AracGrupKod, int? MinGun, int? MaxGun, decimal? Iskonto, decimal? HaftaSonuFarkOran, decimal? SonraOdeOran,
    int? HediyeGun, bool KampanyaMi, string? KampanyaKodu, string? MusteriSegment, DateTimeOffset? GecerlilikBas,
    DateTimeOffset? GecerlilikBit, string? SartMetni, DateTimeOffset? TalepBas, DateTimeOffset? TalepBit,
    string? PromosyonTuru, string? KuponGecerlilik, string? HesaplamaTipi, bool HizliIslem, string? HaftaGunKisiti,
    string? TarihTipi, string? KampanyaDurum, bool Aktif = true, string? Surum = null) : ICatalogRequest;
