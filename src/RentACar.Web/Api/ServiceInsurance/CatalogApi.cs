using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Pricing;
using RentACar.Application.TarifeGruplari;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Api.Rezervasyon;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — fiyat/tarife tanım uçları: <c>/tarifeler</c>, <c>/tarife-gruplari</c>, <c>/sigorta-urunleri</c>,
/// <c>/ek-hizmetler</c> (+ <see cref="CatalogApi"/>'nin kural dosyası: tarife matrisi, kira kuralları, broker yasakları,
/// servis tanımları). Blazor paritesi: okuma + yazma OperationsWrite (fiyat operasyonel yapılandırma), servis de aynı
/// izni ikinci kez doğrular. Tanımlar DEFTERE YAZMAZ; fiyat motoru yalnız aktif/onaylı satırları okur.
/// </summary>
internal static partial class CatalogApi
{
    // Property (not field): static field initialization order across partial files is unspecified.
    private static Permission[] OpsOnly => [Permission.OperationsWrite];

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapCatalog(RateCards);
        v1.MapCatalog(RateGroups);
        v1.MapCatalog(CoverageProducts);
        v1.MapCatalog(ExtraServices);
        v1.MapCatalog(RateMatrices);
        v1.MapCatalog(RentalRules);
        v1.MapCatalog(BrokerBans);
        MapServiceDefinitions(v1);
    }

    private static bool Has(string? value, string q) => value?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true;

    // ------------------------------------------------------------------ tarifeler (rate card)

    private static RateCardInput RateCardInput(RateCardRequest r) => new()
    {
        Kod = r.Kod ?? "", Ad = r.Ad ?? "", Grup = r.Grup ?? "", MinGun = r.MinGun ?? 1, MaxGun = r.MaxGun ?? 9999,
        GunlukUcret = r.GunlukUcret ?? 0m, Doviz = r.Doviz ?? "TRY", GecerliBas = S.Date(r.GecerliBas, "gecerliBas"),
        GecerliBit = S.Date(r.GecerliBit, "gecerliBit"), ScdwDahil = r.ScdwDahil, MiniHasarDahil = r.MiniHasarDahil,
        HirsizlikDahil = r.HirsizlikDahil, ScdwZorunlu = r.ScdwZorunlu, Gosterme = r.Gosterme,
        TarifeGrubuId = r.TarifeGrubuId is { } g && g != Guid.Empty ? g : null, Aktif = r.Aktif,
    };

    private static readonly CatalogSpec<RateCardService, RateCard, RateCardRequest, RateCardDto> RateCards = new()
    {
        Path = "/tarifeler", Tag = "Fiyat & Tarife", NotFoundText = "Tarife bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = RateCardDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(RateCardInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, RateCardInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, old) =>
        {
            S.Text(r.Ad, 128, "ad");
            S.Text(r.Grup, 64, "grup");
            S.IntRange(r.MinGun, 0, 1_000_000, "minGun");
            S.IntRange(r.MaxGun, 0, 1_000_000, "maxGun");
            S.CatalogAmount(r.GunlukUcret, old?.GunlukUcret, "gunlukUcret");
            if (!string.Equals(F5Ortak.Nz(r.Doviz), old?.Doviz, StringComparison.OrdinalIgnoreCase))
                AracFinansOrtak.Doviz(r.Doviz); // NormalizeKodStrict — only when changed (legacy codes stay editable)
            S.Text(r.Doviz, 3, "doviz");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q) || Has(d.Grup, q),
        Sort = SortFieldMap<RateCardDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("grup", x => x.Grup).Alan("minGun", x => x.MinGun).Alan("gunlukUcret", x => x.GunlukUcret).Alan("aktif", x => x.Aktif),
        FieldRules =
        [
            ("Tarife kodu", "kod"), ("'", "kod"), ("Tarife adı", "ad"), ("Araç grubu", "grup"), ("Min gün", "minGun"),
            ("Max gün", "maxGun"), ("Günlük ücret", "gunlukUcret"), ("Geçerlilik bitişi", "gecerliBit"),
            ("Seçilen tarife grubu", "tarifeGrubuId"),
        ],
    };

    // ------------------------------------------------------------------ tarife grupları

    private static TarifeGrubuInput RateGroupInput(RateGroupRequest r) => new()
    { Kod = r.Kod ?? "", Ad = r.Ad ?? "", Oran = r.Oran ?? 0m, KullaniciAdi = r.KullaniciAdi, Sifre = r.Sifre, Aktif = r.Aktif };

    private static readonly CatalogSpec<TariffGroupService, TarifeGrubu, RateGroupRequest, RateGroupDto> RateGroups = new()
    {
        Path = "/tarife-gruplari", Tag = "Fiyat & Tarife", NotFoundText = "Tarife grubu bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = RateGroupDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(RateGroupInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, RateGroupInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, old) =>
        {
            S.Text(r.Ad, 128, "ad");
            S.Text(r.KullaniciAdi, 128, "kullaniciAdi");
            S.Text(r.Sifre, 128, "sifre");
            S.Ratio(r.Oran, old?.Oran, "oran");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q),
        Sort = SortFieldMap<RateGroupDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("oran", x => x.Oran).Alan("aktif", x => x.Aktif),
        FieldRules = [("Tarife grubu kodu", "kod"), ("'", "kod"), ("Tarife grubu adı", "ad"), ("Oran", "oran"), ("Şifre", "sifre")],
    };

    // ------------------------------------------------------------------ sigorta ürünleri

    private static CoverageProductInput CoverageInput(CoverageProductRequest r) => new()
    {
        Kod = r.Kod ?? "", Ad = r.Ad ?? "", AdEn = r.AdEn, Aciklama = r.Aciklama,
        Tur = F5Ortak.EnumAdi<CoverageProductType>(r.Tur, "tur") ?? CoverageProductType.Diger,
        GunlukUcret = r.GunlukUcret, KdvOrani = r.KdvOrani, MaxGun = r.MaxGun, Doviz = r.Doviz, Zorunlu = r.Zorunlu, Aktif = r.Aktif,
    };

    private static readonly CatalogSpec<CoverageProductService, CoverageProduct, CoverageProductRequest, CoverageProductDto> CoverageProducts = new()
    {
        Path = "/sigorta-urunleri", Tag = "Fiyat & Tarife", NotFoundText = "Sigorta ürünü bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = CoverageProductDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(CoverageInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, CoverageInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, old) =>
        {
            S.Text(r.Ad, 128, "ad");
            S.Text(r.AdEn, 128, "adEn");
            S.Text(r.Aciklama, 512, "aciklama");
            S.Text(r.Doviz, 8, "doviz");
            S.IntRange(r.MaxGun, 0, 1_000_000, "maxGun");
            S.CatalogAmount(r.GunlukUcret, old?.GunlukUcret, "gunlukUcret");
            S.Ratio(r.KdvOrani, old?.KdvOrani, "kdvOrani");
            F5Ortak.EnumAdi<CoverageProductType>(r.Tur, "tur");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q) || Has(d.AdEn, q),
        Sort = SortFieldMap<CoverageProductDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("tur", x => x.Tur).Alan("gunlukUcret", x => x.GunlukUcret).Alan("aktif", x => x.Aktif),
        FieldRules =
        [
            ("Ürün kodu", "kod"), ("'", "kod"), ("Ürün adı", "ad"), ("Günlük ücret", "gunlukUcret"), ("KDV oranı", "kdvOrani"),
            ("Max gün", "maxGun"),
        ],
    };

    // ------------------------------------------------------------------ ek hizmetler

    private static EkHizmetTanimInput ExtraInput(ExtraServiceRequest r) => new()
    {
        Kod = r.Kod ?? "", Ad = r.Ad ?? "", BirimUcret = r.BirimUcret ?? 0m, KdvOrani = r.KdvOrani ?? 0.20m,
        Aciklama = r.Aciklama, MaxGun = r.MaxGun, Aktif = r.Aktif,
    };

    private static readonly CatalogSpec<AddOnDefinitionService, EkHizmetTanim, ExtraServiceRequest, ExtraServiceDto> ExtraServices = new()
    {
        Path = "/ek-hizmetler", Tag = "Fiyat & Tarife", NotFoundText = "Ek hizmet bulunamadı.",
        ReadPermissions = OpsOnly, WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = ExtraServiceDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(ExtraInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, ExtraInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, old) =>
        {
            S.Text(r.Ad, 128, "ad");
            S.IntRange(r.MaxGun, 0, 1_000_000, "maxGun");
            S.CatalogAmount(r.BirimUcret, old?.BirimUcret, "birimUcret");
            S.Ratio(r.KdvOrani, old?.KdvOrani, "kdvOrani");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.Ad, q),
        Sort = SortFieldMap<ExtraServiceDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad)
            .Alan("birimUcret", x => x.BirimUcret).Alan("aktif", x => x.Aktif),
        FieldRules =
        [
            ("Ek hizmet kodu", "kod"), ("'", "kod"), ("SYS-", "kod"), ("Sistem ücret tanımının", "kod"), ("Ek hizmet adı", "ad"),
            ("Birim ücret", "birimUcret"), ("KDV oranı", "kdvOrani"), ("Max gün", "maxGun"), ("Açıklama", "aciklama"),
        ],
    };
}
