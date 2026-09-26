using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Blog;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

public static partial class WebsiteApi
{
    private const string BlogRoot = UiApiExtensions.V1 + "/blog-yonetim";

    /// <summary>
    /// Blog yönetimi (Blazor <c>BlogList</c>/<c>BlogOnizleme</c>). İçerik düz metindir; önizleme HTML değil blok listesi
    /// döner (sitedeki <see cref="ContentText.Blocks"/> kuralının aynısı). Kapak içerikten tür tespitiyle (PNG/JPEG/WebP)
    /// ve 2 MB sınırıyla serviste doğrulanır.
    /// </summary>
    private static void MapBlog(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/blog-yonetim").WithTags(SystemApiCommon.WebsiteTag).RequirePermission(Permission.OperationsWrite);

        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, BlogService s, CancellationToken ct) =>
            TypedResults.Ok(F5Ortak.Sayfala((await s.ListAllAsync(ct))
                .Select(p => new BlogRowDto(p.Id, p.Baslik, p.Slug, p.Ozet, p.Durum.ToString(), p.YayinTarihi, p.KapakVar,
                    p.AltBaslik, p.AramaDisi)).ToList(), BlogSort, sayfa, boyut, sirala)))
            .AlanlariEsle(F5Ortak.SiralamaKurallari);

        g.MapGet("/{id:guid}", async Task<Results<Ok<BlogDetailDto>, ProblemHttpResult>> (Guid id, BlogService s, CancellationToken ct)
            => await BlogAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Yazı bulunamadı."));

        g.MapPost("", async Task<Results<Created<BlogDetailDto>, ProblemHttpResult>> (BlogRequest i, BlogService s, CancellationToken ct) =>
        {
            var input = ToBlogInput(i);
            var id = await s.CreateAsync(input, ct);
            return await BlogAsync(id, s, ct) is { } d
                ? TypedResults.Created($"{BlogRoot}/{id}", d) : SystemApiCommon.NotFound("Yazı bulunamadı.");
        }).AlanlariEsle(BlogRules);

        g.MapPut("/{id:guid}", async Task<Results<Ok<BlogDetailDto>, ProblemHttpResult>> (
            Guid id, BlogRequest i, BlogService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("Yazı bulunamadı.");
            SystemApiCommon.RequireVersion(i.Surum);
            var input = ToBlogInput(i);
            if (!await s.UpdateAsync(id, input, i.Surum!, ct)) return SystemApiCommon.NotFound("Yazı bulunamadı.");
            return await BlogAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Yazı bulunamadı.");
        }).AlanlariEsle(BlogRules);

        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, BlogService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound("Yazı bulunamadı."));

        g.MapGet("/{id:guid}/onizleme", async Task<Results<Ok<BlogPreviewDto>, ProblemHttpResult>> (
            Guid id, BlogService s, CancellationToken ct)
            => await s.GetAsync(id, ct) is { } p ? TypedResults.Ok(Preview(p)) : SystemApiCommon.NotFound("Yazı bulunamadı."));

        // ---- kapak
        g.MapPost("/{id:guid}/kapak", UploadCover).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadRequestLimit))
            .AlanlariEsle(CoverRules);

        g.MapDelete("/{id:guid}/kapak", async Task<Results<Ok<BlogDetailDto>, ProblemHttpResult>> (
            Guid id, BlogService s, CancellationToken ct) =>
        {
            if (!await s.SetCoverAsync(id, null, ct)) return SystemApiCommon.NotFound("Yazı bulunamadı.");
            return await BlogAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Yazı bulunamadı.");
        });

        // İçerik satır içi görsel (dosya adı yok → indirme değil). Tür, yüklemede İÇERİKTEN tespit edilip saklanan değerdir;
        // X-Content-Type-Options: nosniff boru hattında her yanıta yazılır.
        g.MapGet("/{id:guid}/kapak", (Guid id, BlogService s, CancellationToken ct) => CoverContent(id, s, thumb: false, ct))
            .CoverResponse();
        g.MapGet("/{id:guid}/kapak/kucuk", (Guid id, BlogService s, CancellationToken ct) => CoverContent(id, s, thumb: true, ct))
            .CoverResponse();
    }

    private static async Task<Results<Created<BlogDetailDto>, ProblemHttpResult>> UploadCover(
        Guid id, IFormFile? kapak, BlogService s, CancellationToken ct)
    {
        if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("Yazı bulunamadı.");
        if (kapak is null || kapak.Length == 0) throw new ValidationException("Kapak görseli seçilmedi.", "kapak");
        if (kapak.Length > UploadRequestLimit) throw new ValidationException("Kapak görseli en fazla 2 MB olabilir.", "kapak");
        using var ms = new MemoryStream();
        await kapak.CopyToAsync(ms, ct);
        if (!await s.SetCoverAsync(id, ms.ToArray(), ct)) return SystemApiCommon.NotFound("Yazı bulunamadı.");
        return await BlogAsync(id, s, ct) is { } d
            ? TypedResults.Created($"{BlogRoot}/{id}/kapak", d) : SystemApiCommon.NotFound("Yazı bulunamadı.");
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> CoverContent(
        Guid id, BlogService s, bool thumb, CancellationToken ct)
    {
        var c = await s.GetCoverAsync(id, ct);
        if (c is null) return SystemApiCommon.NotFound("Kapak bulunamadı.");
        return thumb && c.Thumb is { Length: > 0 } t
            ? TypedResults.File(t, "image/jpeg")
            : TypedResults.File(c.Bytes, c.ContentType);
    }

    /// <summary>Dosya sonucu yanıt metadatası üretmiyor → OpenAPI'ye ikili görsel + 404 problem elle bildirilir.</summary>
    private static RouteHandlerBuilder CoverResponse(this RouteHandlerBuilder b)
        => b.Produces(StatusCodes.Status200OK, typeof(byte[]), "image/png", "image/jpeg", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static BlogInput ToBlogInput(BlogRequest i)
    {
        Sinirlar.Metin(i.Baslik, 200, "baslik", "Başlık");
        Sinirlar.Metin(i.Slug, 200, "slug", "Adres");
        Sinirlar.Metin(i.Ozet, 500, "ozet", "Özet");
        Sinirlar.Metin(i.Icerik, 20_000, "icerik", "İçerik");
        Sinirlar.Metin(i.AltBaslik, 300, "altBaslik", "Alt başlık");
        Sinirlar.Metin(i.SeoBaslik, 300, "seoBaslik", "SEO başlığı");
        Sinirlar.Metin(i.MetaAciklama, 500, "metaAciklama", "Arama açıklaması");
        Sinirlar.Metin(i.AnahtarKelimeler, 500, "anahtarKelimeler", "Anahtar kelimeler");
        Sinirlar.Metin(i.Yazar, 160, "yazar", "Yazar");
        Sinirlar.Metin(i.KapakAlt, 300, "kapakAlt", "Kapak açıklaması");
        return new BlogInput
        {
            Baslik = i.Baslik ?? "",
            Slug = SystemApiCommon.Clean(i.Slug),
            Ozet = SystemApiCommon.Clean(i.Ozet),
            Icerik = i.Icerik ?? "",
            Durum = F5Ortak.EnumAdi<BlogPostDurum>(i.Durum, "durum") ?? BlogPostDurum.Taslak,
            AltBaslik = SystemApiCommon.Clean(i.AltBaslik),
            SeoBaslik = SystemApiCommon.Clean(i.SeoBaslik),
            MetaAciklama = SystemApiCommon.Clean(i.MetaAciklama),
            AnahtarKelimeler = SystemApiCommon.Clean(i.AnahtarKelimeler),
            Yazar = SystemApiCommon.Clean(i.Yazar),
            KapakAlt = SystemApiCommon.Clean(i.KapakAlt),
            AramaDisi = i.AramaDisi,
        };
    }

    private static readonly (string, string)[] BlogRules =
        [("Başlık zorunlu", "baslik"), ("İçerik zorunlu", "icerik"), ("Başlıktan geçerli", "slug"), ("'", "slug")];

    private static readonly (string, string)[] CoverRules =
        [("PNG, JPEG veya WebP", "kapak"), ("Kapak görseli", "kapak")];

    /// <summary>Sürüm alanlardan ÖNCE okunur. Kapak baytları DTO'ya girmez (ayrı uç).</summary>
    private static async Task<BlogDetailDto?> BlogAsync(Guid id, BlogService s, CancellationToken ct)
    {
        var version = await s.VersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } p
            ? new BlogDetailDto(p.Id, p.Baslik, p.Slug, p.Ozet, p.Icerik, p.Durum.ToString(), p.YayinTarihi, p.KapakBytes is not null,
                p.AltBaslik, p.SeoBaslik, p.MetaAciklama, p.AnahtarKelimeler, p.Yazar, p.KapakAlt, p.AramaDisi,
                p.YayinTarihi is not null, version)
            : null;
    }

    /// <summary>Blazor <c>BlogOnizleme</c> ile aynı öncelikler: arama başlığı SeoBaslik &gt; Baslik; açıklama
    /// MetaAciklama &gt; Ozet &gt; gövdeden türetilen özet.</summary>
    private static BlogPreviewDto Preview(BlogPost p)
    {
        var detail = new BlogDetail(p.Id, p.Baslik, p.Slug, p.Ozet, p.Icerik, p.Durum, p.YayinTarihi, p.KapakBytes is not null,
            p.AltBaslik, p.SeoBaslik, p.MetaAciklama, p.AnahtarKelimeler, p.Yazar, p.KapakAlt, p.AramaDisi);
        var description = !string.IsNullOrWhiteSpace(p.MetaAciklama) ? p.MetaAciklama
            : !string.IsNullOrWhiteSpace(p.Ozet) ? p.Ozet
            : ContentText.Summary(p.Icerik);
        return new BlogPreviewDto(p.Id, p.Durum.ToString(), "/blog/" + p.Slug, p.Baslik, p.AltBaslik, detail.AramaBasligi,
            description, detail.Kelimeler, p.AramaDisi,
            ContentText.Blocks(p.Icerik).Select(b => new ContentBlockDto(b.Tur.ToString(), b.Metin)).ToList());
    }

    private static readonly SortFieldMap<BlogRowDto> BlogSort = SortFieldMap<BlogRowDto>
        .Create(x => x.Id).Alan("baslik", x => x.Baslik).Alan("durum", x => x.Durum).Alan("yayinTarihi", x => x.YayinTarihi);
}
