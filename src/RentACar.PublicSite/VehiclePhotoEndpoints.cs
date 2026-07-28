using Microsoft.Net.Http.Headers;
using RentACar.Application.Vehicles;

namespace RentACar.PublicSite;

/// <summary>
/// PR-4: halka açık araç fotoğrafı serve uçları. `RentACar.Web/Vehicles/VehiclePhotoEndpoints.cs`'in
/// okuma-ucu deseniyle (ETag/304/Cache-Control) AYNI, ama VehicleId route'ta YOK — izolasyon tamamen
/// RLS'e dayanır (bkz. VehiclePhotoService.GetPublicAsync/GetPublicThumbAsync doc-yorumu). Kimlik
/// doğrulaması middleware'i bu projede yok → uçlar doğal olarak anonim.
/// </summary>
public static class VehiclePhotoEndpoints
{
    public static IEndpointRouteBuilder MapVehiclePhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/foto/{photoId:guid}", async (Guid photoId, HttpRequest req, HttpResponse res, VehiclePhotoService svc)
            => await ServeAsync(photoId, "", req, res, svc.GetPublicAsync));

        app.MapGet("/foto/{photoId:guid}/thumb", async (Guid photoId, HttpRequest req, HttpResponse res, VehiclePhotoService svc)
            => await ServeAsync(photoId, "-t", req, res, svc.GetPublicThumbAsync));

        return app;
    }

    private static async Task<IResult> ServeAsync(
        Guid photoId, string etagSuffix, HttpRequest req, HttpResponse res,
        Func<Guid, CancellationToken, Task<VehiclePhotoContent?>> fetch)
    {
        var etag = $"\"{photoId}{etagSuffix}\"";
        if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var reqTags)
            && reqTags.Any(t => t.Tag == etag))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        var p = await fetch(photoId, req.HttpContext.RequestAborted);
        if (p is null) return Results.NotFound();

        res.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.Bytes(p.Bytes, p.ContentType, entityTag: new EntityTagHeaderValue(etag));
    }
}
