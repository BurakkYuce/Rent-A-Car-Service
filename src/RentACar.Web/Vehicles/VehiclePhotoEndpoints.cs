using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Vehicles;

/// <summary>
/// Araç fotoğraf galerisi uçları (PR-3). Yazma (yükle/sil/sırala) OperationsWrite gerektirir; OKUMA
/// (serve) AYRI bir grupta — yalnız kimlik doğrulaması (VehiclePhotoService.GetBytesAsync/GetThumbAsync
/// zaten VehicleService.GetAsync ile aynı guard'ı uyguluyor: PermissionGuard yok, yalnız BranchScope).
/// </summary>
public static class VehiclePhotoEndpoints
{
    public static IEndpointRouteBuilder MapVehiclePhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var write = app.MapGroup("/vehicles").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        write.MapPost("/{id:guid}/photos", async (Guid id, IFormFile? foto, VehiclePhotoService svc) =>
        {
            if (foto is null || foto.Length == 0) return Result.Error($"/vehicles/{id}", "Fotoğraf seçilmedi.");
            using var ms = new MemoryStream();
            await foto.CopyToAsync(ms);
            try { await svc.AddAsync(id, ms.ToArray()); return Result.Ok($"/vehicles/{id}", "Fotoğraf yüklendi."); }
            catch (ValidationException ex) { return Results.Redirect($"/vehicles/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        }).WithMetadata(new RequestSizeLimitAttribute(3_000_000)); // 2 MB foto cap + multipart payı; Kestrel'in ~30 MB varsayılanından çok daha sıkı

        write.MapPost("/{id:guid}/photos/{photoId:guid}/sil", async (Guid id, Guid photoId, VehiclePhotoService svc) =>
        {
            await svc.DeleteAsync(id, photoId);
            return Result.Ok($"/vehicles/{id}", "Kayıt silindi.");
        });

        write.MapPost("/{id:guid}/photos/{photoId:guid}/yukari", async (Guid id, Guid photoId, VehiclePhotoService svc) =>
        {
            await svc.MoveAsync(id, photoId, -1);
            return Result.Ok($"/vehicles/{id}", "Sıra güncellendi.");
        });

        write.MapPost("/{id:guid}/photos/{photoId:guid}/asagi", async (Guid id, Guid photoId, VehiclePhotoService svc) =>
        {
            await svc.MoveAsync(id, photoId, 1);
            return Result.Ok($"/vehicles/{id}", "Sıra güncellendi.");
        });

        // Okuma — OperationsWrite DEĞİL (VehicleService.GetAsync'in servis-seviyesi guard'ıyla tutarlı).
        var read = app.MapGroup("/vehicles").RequireAuthorization();

        read.MapGet("/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, HttpRequest req, HttpResponse res, VehiclePhotoService svc)
            => await ServeAsync(id, photoId, "", req, res, svc.GetBytesAsync));

        read.MapGet("/{id:guid}/photos/{photoId:guid}/thumb", async (Guid id, Guid photoId, HttpRequest req, HttpResponse res, VehiclePhotoService svc)
            => await ServeAsync(id, photoId, "-t", req, res, svc.GetThumbAsync));

        return app;
    }

    /// <summary>ETag `photoId`den (+ ayırt edici son ek) türer — URL'den bilinen bir değer, `If-None-Match`
    /// eşleşiyorsa DB'ye HİÇ gitmeden 304 döner. Satırlar immutable → uzun ömürlü cache güvenli.</summary>
    private static async Task<IResult> ServeAsync(
        Guid vehicleId, Guid photoId, string etagSuffix, HttpRequest req, HttpResponse res,
        Func<Guid, Guid, CancellationToken, Task<VehiclePhotoContent?>> fetch)
    {
        var etag = $"\"{photoId}{etagSuffix}\"";
        if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var reqTags)
            && reqTags.Any(t => t.Tag == etag))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        var p = await fetch(vehicleId, photoId, req.HttpContext.RequestAborted);
        if (p is null) return Results.NotFound();

        res.Headers.CacheControl = "private, max-age=31536000, immutable";
        return Results.Bytes(p.Bytes, p.ContentType, entityTag: new EntityTagHeaderValue(etag));
    }
}
