using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.ServisTanimlari;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class CatalogApi
{
    // ------------------------------------------------------------------ tarife matrisi

    /// <summary>
    /// Approval stamp (server side): <c>Onayli</c> reached now → session user + now; unchanged state → stored values;
    /// any other state → cleared. Client-sent approver/time are never trusted.
    /// </summary>
    private static RateMatrixInput RateMatrixInput(RateMatrixRequest r, RateMatrix? old, HttpContext http)
    {
        var state = F5Ortak.EnumAdi<TariffApprovalStatus>(r.OnayDurumu, "onayDurumu") ?? TariffApprovalStatus.Bekliyor;
        string? approver = null;
        DateTimeOffset? approvedAt = null;
        if (state == TariffApprovalStatus.Onayli)
        {
            if (old is { OnayDurumu: TariffApprovalStatus.Onayli }) (approver, approvedAt) = (old.Onaylayan, old.OnayZaman);
            else
            {
                var user = http.RequestServices.GetRequiredService<ICurrentUser>();
                (approver, approvedAt) = (user.UserName, DateTimeOffset.UtcNow);
            }
        }
        return new RateMatrixInput
        {
            Kod = r.Kod ?? "", Ad = r.Ad ?? "", Aciklama = r.Aciklama, Kanal = r.Kanal, Sube = r.Sube, Lokasyon = r.Lokasyon,
            Turu = r.Turu, KiraSuresi = r.KiraSuresi, AracGrupKod = r.AracGrupKod, ParaBirimi = r.ParaBirimi,
            BasTar = S.Date(r.BasTar, "basTar"), BitTar = S.Date(r.BitTar, "bitTar"),
            Gun1 = r.Gun1, Gun2 = r.Gun2, Gun3 = r.Gun3, Gun4 = r.Gun4, Gun5 = r.Gun5, Gun6 = r.Gun6, Gun7 = r.Gun7,
            GunHaftalik = r.GunHaftalik, GunAylik = r.GunAylik,
            Km1 = r.Km1, Km2 = r.Km2, Km3 = r.Km3, Km4 = r.Km4, Km5 = r.Km5, Km6 = r.Km6,
            Km1Ucret = r.Km1Ucret, Km2Ucret = r.Km2Ucret, Km3Ucret = r.Km3Ucret, Km4Ucret = r.Km4Ucret,
            Km5Ucret = r.Km5Ucret, Km6Ucret = r.Km6Ucret, KmHaftalik = r.KmHaftalik, KmHaftalikUcret = r.KmHaftalikUcret,
            KmAylik = r.KmAylik, KmAylikUcret = r.KmAylikUcret, MaxEsneklik = r.MaxEsneklik,
            OnayDurumu = state, Onaylayan = approver, OnayZaman = approvedAt, Aktif = r.Aktif,
        };
    }

    private static void RateMatrixLimits(RateMatrixRequest r, RateMatrix? o)
    {
        S.Text(r.Ad, 128, "ad"); S.Text(r.Aciklama, 512, "aciklama"); S.Text(r.Kanal, 64, "kanal"); S.Text(r.Sube, 64, "sube");
        S.Text(r.Lokasyon, 64, "lokasyon"); S.Text(r.Turu, 32, "turu"); S.Text(r.AracGrupKod, 32, "aracGrupKod");
        S.Text(r.ParaBirimi, 8, "paraBirimi");
        S.IntRange(r.KiraSuresi, 0, 100_000, "kiraSuresi");
        S.CatalogAmount(r.Gun1, o?.Gun1, "gun1"); S.CatalogAmount(r.Gun2, o?.Gun2, "gun2"); S.CatalogAmount(r.Gun3, o?.Gun3, "gun3");
        S.CatalogAmount(r.Gun4, o?.Gun4, "gun4"); S.CatalogAmount(r.Gun5, o?.Gun5, "gun5"); S.CatalogAmount(r.Gun6, o?.Gun6, "gun6");
        S.CatalogAmount(r.Gun7, o?.Gun7, "gun7");
        S.CatalogAmount(r.GunHaftalik, o?.GunHaftalik, "gunHaftalik"); S.CatalogAmount(r.GunAylik, o?.GunAylik, "gunAylik");
        foreach (var (v, f) in new[] { (r.Km1, "km1"), (r.Km2, "km2"), (r.Km3, "km3"), (r.Km4, "km4"), (r.Km5, "km5"),
                     (r.Km6, "km6"), (r.KmHaftalik, "kmHaftalik"), (r.KmAylik, "kmAylik") })
            S.IntRange(v, 0, 10_000_000, f);
        S.CatalogAmount(r.Km1Ucret, o?.Km1Ucret, "km1Ucret"); S.CatalogAmount(r.Km2Ucret, o?.Km2Ucret, "km2Ucret");
        S.CatalogAmount(r.Km3Ucret, o?.Km3Ucret, "km3Ucret"); S.CatalogAmount(r.Km4Ucret, o?.Km4Ucret, "km4Ucret");
        S.CatalogAmount(r.Km5Ucret, o?.Km5Ucret, "km5Ucret"); S.CatalogAmount(r.Km6Ucret, o?.Km6Ucret, "km6Ucret");
        S.CatalogAmount(r.KmHaftalikUcret, o?.KmHaftalikUcret, "kmHaftalikUcret");
        S.CatalogAmount(r.KmAylikUcret, o?.KmAylikUcret, "kmAylikUcret");
        S.Ratio(r.MaxEsneklik, o?.MaxEsneklik, "maxEsneklik");
        F5Ortak.EnumAdi<TariffApprovalStatus>(r.OnayDurumu, "onayDurumu");
    }

    private static readonly CatalogSpec<RateMatrixService, RateMatrix, RateMatrixRequest, RateMatrixDto> RateMatrices = new()
    {
        Path = "/tarife-matris", Tag = "Fiyat & Tarife", NotFoundText = "Tarife matrisi satırı bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = RateMatrixDto.From,
        Create = (s, r, http, ct) => s.CreateAsync(RateMatrixInput(r, null, http), ct),
        Update = (s, id, r, old, v, http, ct) => s.UpdateVersionedAsync(id, RateMatrixInput(r, old, http), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = RateMatrixLimits,
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q) || Has(d.Kanal, q) || Has(d.AracGrupKod, q),
        Sort = SortFieldMap<RateMatrixDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("kanal", x => x.Kanal).Alan("aracGrupKod", x => x.AracGrupKod).Alan("gun1", x => x.Gun1)
            .Alan("onayDurumu", x => x.OnayDurumu).Alan("aktif", x => x.Aktif),
        FieldRules =
        [
            ("Tarife kodu", "kod"), ("'", "kod"), ("Tarife adı", "ad"), ("Max kira kapsamı", "kiraSuresi"),
            ("Gün 1", "gun1"), ("Gün 2", "gun2"), ("Gün 3", "gun3"), ("Gün 4", "gun4"), ("Gün 5", "gun5"), ("Gün 6", "gun6"),
            ("Gün 7", "gun7"), ("Haftalık kademe (8-29 gün)", "gunHaftalik"), ("Aylık kademe (30+ gün)", "gunAylik"),
            ("Esneklik", "maxEsneklik"), ("Bitiş tarihi", "bitTar"),
        ],
    };

    // ------------------------------------------------------------------ kira kuralları

    private static RentalRuleInput RentalRuleInput(RentalRuleRequest r) => new()
    {
        Kod = r.Kod ?? "", Ad = r.Ad ?? "", Aciklama = r.Aciklama, Kanal = r.Kanal, Sube = r.Sube, AracGrupKod = r.AracGrupKod,
        MinGun = r.MinGun, MaxGun = r.MaxGun, Iskonto = r.Iskonto, HaftaSonuFarkOran = r.HaftaSonuFarkOran,
        SonraOdeOran = r.SonraOdeOran, HediyeGun = r.HediyeGun, KampanyaMi = r.KampanyaMi, KampanyaKodu = r.KampanyaKodu,
        MusteriSegment = r.MusteriSegment, GecerlilikBas = S.Date(r.GecerlilikBas, "gecerlilikBas"),
        GecerlilikBit = S.Date(r.GecerlilikBit, "gecerlilikBit"), SartMetni = r.SartMetni,
        TalepBas = S.Date(r.TalepBas, "talepBas"), TalepBit = S.Date(r.TalepBit, "talepBit"),
        PromosyonTuru = F5Ortak.EnumAdi<PromotionType>(r.PromosyonTuru, "promosyonTuru"),
        KuponGecerlilik = F5Ortak.EnumAdi<CouponValidity>(r.KuponGecerlilik, "kuponGecerlilik"),
        HesaplamaTipi = F5Ortak.EnumAdi<CalculationType>(r.HesaplamaTipi, "hesaplamaTipi"), HizliIslem = r.HizliIslem,
        HaftaGunKisiti = r.HaftaGunKisiti,
        TarihTipi = F5Ortak.EnumAdi<RuleDateType>(r.TarihTipi, "tarihTipi") ?? RuleDateType.Rezervasyon,
        KampanyaDurum = F5Ortak.EnumAdi<CampaignStatus>(r.KampanyaDurum, "kampanyaDurum"), Aktif = r.Aktif,
    };

    private static readonly CatalogSpec<RentalRuleService, RentalRule, RentalRuleRequest, RentalRuleDto> RentalRules = new()
    {
        Path = "/kira-kurallari", Tag = "Fiyat & Tarife", NotFoundText = "Kiralama kuralı bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = RentalRuleDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(RentalRuleInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, RentalRuleInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, o) =>
        {
            S.Text(r.Ad, 128, "ad"); S.Text(r.Aciklama, 512, "aciklama"); S.Text(r.Kanal, 64, "kanal"); S.Text(r.Sube, 64, "sube");
            S.Text(r.AracGrupKod, 32, "aracGrupKod"); S.Text(r.SartMetni, 4000, "sartMetni"); S.Text(r.HaftaGunKisiti, 32, "haftaGunKisiti");
            S.IntRange(r.MinGun, 0, 100_000, "minGun"); S.IntRange(r.MaxGun, 0, 100_000, "maxGun");
            S.IntRange(r.HediyeGun, 0, 100_000, "hediyeGun");
            S.Ratio(r.Iskonto, o?.Iskonto, "iskonto"); S.Ratio(r.HaftaSonuFarkOran, o?.HaftaSonuFarkOran, "haftaSonuFarkOran");
            S.Ratio(r.SonraOdeOran, o?.SonraOdeOran, "sonraOdeOran");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q) || Has(d.KampanyaKodu, q),
        Sort = SortFieldMap<RentalRuleDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("kanal", x => x.Kanal).Alan("kampanyaDurum", x => x.KampanyaDurum).Alan("gecerlilikBas", x => x.GecerlilikBas),
        FieldRules =
        [
            ("Kural kodu", "kod"), ("'", "kampanyaKodu"), ("Kampanya kodu", "kampanyaKodu"), ("Müşteri segmenti", "musteriSegment"),
            ("Kural adı", "ad"), ("Min gün", "minGun"), ("Max gün", "maxGun"), ("Hediye gün", "hediyeGun"), ("İskonto", "iskonto"),
            ("Hafta sonu", "haftaSonuFarkOran"), ("Sonra öde", "sonraOdeOran"), ("Geçerlilik bitişi", "gecerlilikBit"),
            ("Talep bitişi", "talepBit"), ("Geçersiz hafta günü", "haftaGunKisiti"),
        ],
    };

    // ------------------------------------------------------------------ broker yasakları

    private static BrokerYasakInput BrokerInput(BrokerBanRequest r) => new()
    {
        Kod = r.Kod ?? "", Ad = r.Ad ?? "", Aciklama = r.Aciklama, Kaynak = r.Kaynak, AracGrupKod = r.AracGrupKod,
        Bolge = r.Bolge, MinGun = r.MinGun, TumSatisKapali = r.TumSatisKapali,
        GecerlilikBas = S.Date(r.GecerlilikBas, "gecerlilikBas"), GecerlilikBit = S.Date(r.GecerlilikBit, "gecerlilikBit"),
        Aktif = r.Aktif,
    };

    private static readonly CatalogSpec<BrokerBanService, BrokerYasak, BrokerBanRequest, BrokerBanDto> BrokerBans = new()
    {
        Path = "/broker-yasaklari", Tag = "Fiyat & Tarife", NotFoundText = "Broker yasağı bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = BrokerBanDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(BrokerInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, BrokerInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, _) =>
        {
            S.Text(r.Ad, 128, "ad"); S.Text(r.Aciklama, 512, "aciklama"); S.Text(r.Kaynak, 64, "kaynak");
            S.Text(r.AracGrupKod, 32, "aracGrupKod"); S.Text(r.Bolge, 64, "bolge"); S.IntRange(r.MinGun, -100_000, 100_000, "minGun");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q) || Has(d.Kaynak, q),
        Sort = SortFieldMap<BrokerBanDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("kaynak", x => x.Kaynak).Alan("aktif", x => x.Aktif),
        FieldRules =
        [
            ("Yasak kodu", "kod"), ("'", "kod"), ("Yasak adı", "ad"), ("Min gün", "minGun"), ("Geçerlilik bitişi", "gecerlilikBit"),
            ("En az bir kısıt", "minGun"),
        ],
    };
}
