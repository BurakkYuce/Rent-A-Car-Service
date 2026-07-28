using Microsoft.Net.Http.Headers;
using RentACar.Application.Blog;

namespace RentACar.PublicSite;

/// <summary>
/// PR-6: halka açık blog kapak görseli serve ucu. `VehiclePhotoEndpoints`'in ETag/304 desenini izler,
/// AMA `immutable` cache KULLANMAZ: araç fotoğrafı hiç değişmez (silinip yeniden eklenir), blog kapağı
/// DEĞİŞTİRİLEBİLİR — `immutable` + yalnız-Id ETag ile eski kapak tarayıcı/ara-katman cache'inde süresiz
/// kalırdı. ETag `UpdatedAtUtc`'den türer → kapak değişince ETag de değişir.
///
/// `Durum == Yayinda` filtresi servis/repo katmanında ZORUNLU (RLS taslak/yayında ayrımını bilmez).
/// </summary>
public static class BlogEndpoints
{
    public static IEndpointRouteBuilder MapBlogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/blog-kapak/{postId:guid}", async (Guid postId, HttpRequest req, HttpResponse res, BlogService svc) =>
        {
            var cover = await svc.GetPublishedCoverAsync(postId, req.HttpContext.RequestAborted);
            if (cover is null) return Results.NotFound();

            var etag = $"\"{postId}-{cover.UpdatedAtUtc.Ticks}\"";
            if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var reqTags)
                && reqTags.Any(t => t.Tag == etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            res.Headers.CacheControl = "public, max-age=3600"; // ölçülü: kapak değişebilir → immutable DEĞİL
            var thumb = cover.Thumb is { Length: > 0 };
            return Results.Bytes(thumb ? cover.Thumb! : cover.Bytes, thumb ? "image/jpeg" : cover.ContentType,
                entityTag: new EntityTagHeaderValue(etag));
        });

        return app;
    }
}
