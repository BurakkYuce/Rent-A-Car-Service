using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DolulukFiyat;
using RentACar.Application.DropTanimlari;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — tariff definitions that feed the price engine but post no money: drop matrix (<c>/drop-tanimlari</c>)
/// and occupancy surcharge rules (<c>/doluluk-kurallari</c> + bulk ladder <c>/doluluk-kurallari/toplu</c>).
/// OperationsWrite like the Blazor pages; tenant-wide lists (the Blazor pages apply no branch scope either).
/// </summary>
public static partial class TanimApi
{
    private static void MapPricingDefinitions(RouteGroupBuilder v1)
    {
        v1.MapDefinition(new DefinitionRoute<DropTanim, DropDto, DropRequest>
        {
            Path = "/drop-tanimlari", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<DropDefinitionService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<DropDefinitionService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<DropDefinitionService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<DropDefinitionService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<DropDefinitionService>(sp).CreateAsync(DropInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<DropDefinitionService>(sp).UpdateAsync(id, DropInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<DropDefinitionService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new DropDto(e.Id, e.Lokasyon, e.Sube, e.CikisLokasyon, e.KarsilamaSekli, e.CalismaSekli,
                e.OzelIletisim, e.Ucret, e.MinGun, e.ManSuresi, e.Drop2, e.Aktif, v),
            Sort = DropDto.Sort,
            SearchText = r => [r.Lokasyon, r.Sube, r.CikisLokasyon],
            ValidateLimits = r =>
            {
                // Blazor adversarial M8: a missing "aktif" silently deactivated the row (drop fee stopped).
                if (r.Aktif is null) throw new ValidationException("Durum (aktif) zorunludur.", "aktif");
                Sinirlar.Metin(r.Lokasyon, 150, "lokasyon", "Dönüş lokasyonu");
                Sinirlar.Metin(r.Sube, 150, "sube", "Şube");
                Sinirlar.Metin(r.CikisLokasyon, 150, "cikisLokasyon", "Çıkış lokasyonu");
                Sinirlar.Metin(r.KarsilamaSekli, 100, "karsilamaSekli", "Karşılama şekli");
                Sinirlar.Metin(r.CalismaSekli, 100, "calismaSekli", "Çalışma şekli");
                Sinirlar.Metin(r.OzelIletisim, 200, "ozelIletisim", "Özel iletişim");
                Sinirlar.Tutar(r.Ucret, "ucret", "Drop ücreti");
                Sinirlar.Tutar(r.Drop2, "drop2", "Drop 2");
                if (r.MinGun is > 3650) throw new ValidationException("Asgari gün en fazla 3650 olabilir.", "minGun");
                if (r.ManSuresi is > 100_000) throw new ValidationException("Karşılama süresi gerçekçi değil.", "manSuresi");
            },
            FieldRules =
            [
                ("Lokasyon", "lokasyon"), ("Şube", "sube"), ("Drop ücreti", "ucret"), ("Drop 2", "drop2"),
                ("Asgari gün", "minGun"), ("Karşılama süresi", "manSuresi"), ("'", "lokasyon"),
            ],
            NotFoundMessage = "Drop tanımı bulunamadı.",
        });

        var rules = v1.MapDefinition(new DefinitionRoute<DolulukFiyatKural, OccupancyRuleDto, OccupancyRuleRequest>
        {
            Path = "/doluluk-kurallari", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<OccupancyPriceRuleService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<OccupancyPriceRuleService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<OccupancyPriceRuleService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<OccupancyPriceRuleService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<OccupancyPriceRuleService>(sp).CreateAsync(OccupancyInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<OccupancyPriceRuleService>(sp).UpdateAsync(id, OccupancyInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<OccupancyPriceRuleService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new OccupancyRuleDto(e.Id, e.Kod, e.Ad, e.AracGrupKod, e.EsikYuzde, e.CarpanYuzde, e.Sube,
                e.SadeceKendiSubeleri, Day(e.GecerlilikBas), Day(e.GecerlilikBit), e.Aktif, v),
            Sort = OccupancyRuleDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.AracGrupKod, r.Sube],
            ValidateLimits = r => OccupancyLimits(r.Kod, r.Ad, r.AracGrupKod, r.Sube),
            FieldRules = OccupancyRules,
            NotFoundMessage = "Doluluk kuralı bulunamadı.",
        });

        // Bulk ladder: N rules in one go; the service validates ALL steps before writing any (no half ladder).
        rules.MapPost("/toplu", async Task<Created<OccupancyBulkResult>> (OccupancyBulkRequest b, OccupancyPriceRuleService s, CancellationToken ct) =>
        {
            OccupancyLimits(b.KodOnEk, b.AdOnEk, b.AracGrupKod, b.Sube);
            if ((b.Kademeler?.Count ?? 0) > 20) throw new ValidationException("En fazla 20 kademe girilebilir.", "kademeler");
            var ids = await s.BulkCreateAsync(new DolulukTopluInput
            {
                KodOnEk = b.KodOnEk ?? "", AdOnEk = b.AdOnEk ?? "", AracGrupKod = b.AracGrupKod, Sube = b.Sube,
                SadeceKendiSubeleri = b.SadeceKendiSubeleri, GecerlilikBas = Start(b.GecerlilikBas),
                GecerlilikBit = Start(b.GecerlilikBit), Aktif = b.Aktif,
                Kademeler = (b.Kademeler ?? []).Select(k => new DolulukKademeSatiri(k.EsikYuzde, k.CarpanYuzde)).ToList(),
            }, ct);
            return TypedResults.Created($"{UiApiExtensions.V1}/doluluk-kurallari", new OccupancyBulkResult(ids));
        }).AlanlariEsle([("Kod ön eki", "kodOnEk"), ("Ad ön eki", "adOnEk"), ("En az bir kademe", "kademeler"),
            .. OccupancyRules.Select(x => (x.Onek, "kademeler"))]);
    }

    private static readonly (string Onek, string Alan)[] OccupancyRules =
    [
        ("Kural kodu", "kod"), ("'", "kod"), ("Kural adı", "ad"), ("Doluluk eşiği", "esikYuzde"),
        ("Fiyat çarpanı", "carpanYuzde"), ("'Sadece kendi şubeleri'", "sube"), ("Geçerlilik bitişi", "gecerlilikBit"),
    ];

    private static void OccupancyLimits(string? kod, string? ad, string? grup, string? sube)
    {
        Sinirlar.Metin(kod, 32, "kod", "Kod");
        Sinirlar.Metin(ad, 128, "ad", "Ad");
        Sinirlar.Metin(grup, 32, "aracGrupKod", "Araç grubu");
        Sinirlar.Metin(sube, 64, "sube", "Şube");
    }

    /// <summary>Validity is a calendar day: stored as the Istanbul midnight (UTC) — the same instant the Blazor date
    /// field produces on the production server; read back as the Istanbul day.</summary>
    private static DateTimeOffset? Start(DateOnly? day) => day is { } d ? F5Ortak.GunBasi(d) : null;

    private static DateOnly? Day(DateTimeOffset? at) => at is { } a ? TenantDay.Day(a) : null;

    private static DropTanimInput DropInputOf(DropRequest b) => new()
    {
        Lokasyon = b.Lokasyon ?? "", Sube = b.Sube ?? "", CikisLokasyon = F(b.CikisLokasyon),
        KarsilamaSekli = F(b.KarsilamaSekli), CalismaSekli = F(b.CalismaSekli), OzelIletisim = F(b.OzelIletisim),
        Ucret = b.Ucret, MinGun = b.MinGun, ManSuresi = b.ManSuresi, Drop2 = b.Drop2, Aktif = b.Aktif ?? true,
    };

    private static DolulukFiyatKuralInput OccupancyInputOf(OccupancyRuleRequest b) => new()
    {
        Kod = b.Kod ?? "", Ad = b.Ad ?? "", AracGrupKod = b.AracGrupKod, EsikYuzde = b.EsikYuzde,
        CarpanYuzde = b.CarpanYuzde, Sube = b.Sube, SadeceKendiSubeleri = b.SadeceKendiSubeleri,
        GecerlilikBas = Start(b.GecerlilikBas), GecerlilikBit = Start(b.GecerlilikBit), Aktif = b.Aktif,
    };
}
