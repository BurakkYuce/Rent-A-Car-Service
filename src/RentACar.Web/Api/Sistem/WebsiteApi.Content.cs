using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.SiteIcerik;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

public static partial class WebsiteApi
{
    private const string ContentRoot = UiApiExtensions.V1 + "/site-icerik";

    /// <summary>
    /// Halka açık site içerik sayfaları + SSS (Blazor <c>SiteIcerikYonetim</c>). Uzunluk sınırları, rezerve adresler ve
    /// slug benzersizliği <see cref="SiteContentService"/>'tedir; uç yalnız alan eşlemesi + sürüm kapısı ekler.
    /// </summary>
    private static void MapContent(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/site-icerik").WithTags(SystemApiCommon.WebsiteTag)
            .RequirePermission(Permission.OperationsWrite).RequireWebsiteModule();

        // ---- sayfalar
        g.MapGet("/sayfalar", async (int? sayfa, int? boyut, string? sirala, SiteContentService s, CancellationToken ct) =>
            TypedResults.Ok(F5Shared.Paginate((await s.ListAsync(ct))
                .Select(p => new PageRowDto(p.Id, p.Slug, p.Baslik, p.Sira, p.Yayinda)).ToList(), PageSort, sayfa, boyut, sirala)))
            .MapFields(F5Shared.SortRules);

        g.MapGet("/sayfalar/{id:guid}", async Task<Results<Ok<PageDetailDto>, ProblemHttpResult>> (
            Guid id, SiteContentService s, CancellationToken ct)
            => await PageAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Sayfa bulunamadı."));

        g.MapPost("/sayfalar", async Task<Results<Created<PageDetailDto>, ProblemHttpResult>> (
            PageRequest i, SiteContentService s, CancellationToken ct) =>
        {
            PageLimits(i);
            var id = await s.SaveAsync(ToInput(null, i), ct);
            return await PageAsync(id, s, ct) is { } d
                ? TypedResults.Created($"{ContentRoot}/sayfalar/{id}", d) : SystemApiCommon.NotFound("Sayfa bulunamadı.");
        }).MapFields(PageRules);

        g.MapPut("/sayfalar/{id:guid}", async Task<Results<Ok<PageDetailDto>, ProblemHttpResult>> (
            Guid id, PageRequest i, SiteContentService s, CancellationToken ct) =>
        {
            if (await s.FetchAsync(id, ct) is null) return SystemApiCommon.NotFound("Sayfa bulunamadı.");
            SystemApiCommon.RequireVersion(i.Surum);
            PageLimits(i);
            await s.SaveAsync(ToInput(id, i), i.Surum, ct);
            return await PageAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Sayfa bulunamadı.");
        }).MapFields(PageRules);

        g.MapPost("/sayfalar/{id:guid}/durum", async Task<Results<Ok<PageDetailDto>, ProblemHttpResult>> (
            Guid id, PublishRequest i, SiteContentService s, CancellationToken ct) =>
        {
            if (await s.FetchAsync(id, ct) is null) return SystemApiCommon.NotFound("Sayfa bulunamadı.");
            await s.PublishStatusAsync(id, i.Yayinda, ct);
            return await PageAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Sayfa bulunamadı.");
        });

        g.MapDelete("/sayfalar/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, SiteContentService s, CancellationToken ct) =>
        {
            if (await s.FetchAsync(id, ct) is null) return SystemApiCommon.NotFound("Sayfa bulunamadı.");
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        });

        // ---- SSS
        g.MapGet("/sss", async (SiteContentService s, CancellationToken ct) =>
            TypedResults.Ok<IReadOnlyList<FaqDto>>((await s.ListFaqAsync(ct))
                .Select(k => new FaqDto(k.Id, k.Soru, k.Cevap, k.Sira, k.Yayinda, null)).ToList()));

        g.MapGet("/sss/{id:guid}", async Task<Results<Ok<FaqDto>, ProblemHttpResult>> (
            Guid id, SiteContentService s, CancellationToken ct)
            => await FaqAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Soru bulunamadı."));

        g.MapPost("/sss", async Task<Results<Created<FaqDto>, ProblemHttpResult>> (
            FaqRequest i, SiteContentService s, CancellationToken ct) =>
        {
            var id = await s.SaveFaqAsync(new SssInput(null, i.Soru ?? "", i.Cevap ?? "", i.Sira, i.Yayinda), ct);
            return await FaqAsync(id, s, ct) is { } d
                ? TypedResults.Created($"{ContentRoot}/sss/{id}", d) : SystemApiCommon.NotFound("Soru bulunamadı.");
        }).MapFields(FaqRules);

        g.MapPut("/sss/{id:guid}", async Task<Results<Ok<FaqDto>, ProblemHttpResult>> (
            Guid id, FaqRequest i, SiteContentService s, CancellationToken ct) =>
        {
            if (await FaqAsync(id, s, ct) is null) return SystemApiCommon.NotFound("Soru bulunamadı.");
            SystemApiCommon.RequireVersion(i.Surum);
            await s.SaveFaqAsync(new SssInput(id, i.Soru ?? "", i.Cevap ?? "", i.Sira, i.Yayinda), i.Surum, ct);
            return await FaqAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Soru bulunamadı.");
        }).MapFields(FaqRules);

        g.MapDelete("/sss/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, SiteContentService s, CancellationToken ct) =>
        {
            if (await FaqAsync(id, s, ct) is null) return SystemApiCommon.NotFound("Soru bulunamadı.");
            await s.DeleteFaqAsync(id, ct);
            return TypedResults.NoContent();
        });
    }

    private static SayfaIcerikInput ToInput(Guid? id, PageRequest i)
        => new(id, i.Baslik ?? "", i.Govde ?? "", i.Slug, i.MetaAciklama, i.Sira, i.Yayinda);

    /// <summary>Slug kolonu 200; diğer uzunluklar serviste (başlık 200, gövde 20.000, özet 300).</summary>
    private static void PageLimits(PageRequest i) => RentalLimits.Text(i.Slug, 200, "slug", "Adres");

    private static readonly (string, string)[] PageRules =
    [
        ("Sayfa başlığı", "baslik"), ("Başlık en çok", "baslik"), ("Sayfa içeriği", "govde"), ("İçerik en çok", "govde"),
        ("Arama motoru özeti", "metaAciklama"), ("Başlıktan bir adres", "slug"), ("\"", "slug"),
    ];

    private static readonly (string, string)[] FaqRules =
        [("Soru zorunlu", "soru"), ("Soru en çok", "soru"), ("Cevap zorunlu", "cevap"), ("Cevap en çok", "cevap")];

    /// <summary>Sürüm alanlardan ÖNCE okunur.</summary>
    private static async Task<PageDetailDto?> PageAsync(Guid id, SiteContentService s, CancellationToken ct)
    {
        var version = await s.VersionAsync(id, ct);
        return await s.FetchAsync(id, ct) is { } p
            ? new PageDetailDto(p.Id, p.Slug, p.Baslik, p.Govde, p.MetaAciklama, p.Sira, p.Yayinda, version)
            : null;
    }

    private static async Task<FaqDto?> FaqAsync(Guid id, SiteContentService s, CancellationToken ct)
    {
        var version = await s.FaqVersionAsync(id, ct);
        return (await s.ListFaqAsync(ct)).FirstOrDefault(k => k.Id == id) is { } k
            ? new FaqDto(k.Id, k.Soru, k.Cevap, k.Sira, k.Yayinda, version)
            : null;
    }

    private static readonly SortFieldMap<PageRowDto> PageSort = SortFieldMap<PageRowDto>
        .Create(x => x.Id).Alan("baslik", x => x.Baslik).Alan("slug", x => x.Slug).Alan("sira", x => x.Sira)
        .Alan("yayinda", x => x.Yayinda);
}
