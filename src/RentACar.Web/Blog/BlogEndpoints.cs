using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RentACar.Application.Authorization;
using RentACar.Application.Blog;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Web.Identity;

namespace RentACar.Web.Blog;

/// <summary>
/// Blog yönetim uçları (PR-6). Yazma + staff listeleme OperationsWrite; kapak önizleme ucu AYRI grupta
/// yalnız kimlik doğrulamasıyla (VehiclePhotoEndpoints deseni — servis guard'ı zaten ayrımı yapar).
/// </summary>
public static class BlogEndpoints
{
    public static IEndpointRouteBuilder MapBlogEndpoints(this IEndpointRouteBuilder app)
    {
        var write = app.MapGroup("/blog-yonetim").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        write.MapPost("/create", async (BlogService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        write.MapPost("/update", async (BlogService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        write.MapPost("/delete", async (BlogService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        // Kapak yükle (multipart). PNG/JPEG/WebP + ≤2 MB serviste doğrulanır (paylaşılan ImageValidation).
        write.MapPost("/{id:guid}/kapak", async (Guid id, IFormFile? kapak, BlogService svc) =>
        {
            if (kapak is null || kapak.Length == 0) return Results.Redirect("/blog-yonetim");
            using var ms = new MemoryStream();
            await kapak.CopyToAsync(ms);
            return await Run(() => svc.SetKapakAsync(id, ms.ToArray()));
        }).WithMetadata(new RequestSizeLimitAttribute(3_000_000)); // 2 MB cap + multipart payı (PR-3 deseni)

        write.MapPost("/{id:guid}/kapak-sil", async (Guid id, BlogService svc) =>
            await Run(() => svc.SetKapakAsync(id, null)));

        // Staff kapak önizlemesi — taslak dahil (public uç AYRI, Durum=Yayinda filtreli).
        var read = app.MapGroup("/blog-yonetim").RequireAuthorization();
        read.MapGet("/{id:guid}/kapak", async (Guid id, HttpRequest req, HttpResponse res, BlogService svc) =>
        {
            var cover = await svc.GetCoverAsync(id, req.HttpContext.RequestAborted);
            if (cover is null) return Results.NotFound();
            res.Headers.CacheControl = "private, max-age=60"; // kapak DEĞİŞEBİLİR → immutable DEĞİL
            return Results.Bytes(cover.Thumb is { Length: > 0 } ? cover.Thumb : cover.Bytes,
                cover.Thumb is { Length: > 0 } ? "image/jpeg" : cover.ContentType,
                entityTag: new EntityTagHeaderValue($"\"{id}-{cover.UpdatedAtUtc.Ticks}\""));
        });

        return app;
    }

    private static BlogInput Build(IFormCollection f) => new()
    {
        Baslik = f["baslik"].ToString(),
        Slug = FormParse.Str(f, "slug"),
        Ozet = FormParse.Str(f, "ozet"),
        Icerik = f["icerik"].ToString(),
        Durum = f["durum"].ToString() == nameof(BlogPostDurum.Yayinda) ? BlogPostDurum.Yayinda : BlogPostDurum.Taslak,
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/blog-yonetim"); }
        catch (ValidationException ex) { return Results.Redirect($"/blog-yonetim?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
