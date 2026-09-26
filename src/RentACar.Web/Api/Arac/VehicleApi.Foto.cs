using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Arac;

public static partial class VehicleApi
{
    /// <summary>İstek gövdesi üst sınırı: 2 MB fotoğraf + multipart payı (Blazor ucuyla aynı).</summary>
    private const long PhotoRequestLimit = 3_000_000;

    /// <summary>
    /// Fotoğraf galerisi (Blazor <c>VehicleEdit</c> galeri bölümü). Okuma: OW VEYA ViewReports; yazma: OW. Her uç ÖNCE
    /// aracın varlığı (404) ve şube kapsamından (403) geçer; foto başka araca aitse 404. Tür (PNG/JPEG/WebP, içerikten
    /// tespit), 2 MB ve araç başına 20 adet sınırı servistedir.
    /// <para>Yükleme <c>multipart/form-data</c> (<c>foto</c> alanı). CSRF grubun başlık filtresinde doğrulanır
    /// (<c>X-XSRF-TOKEN</c>); form bağlamanın ayrı antiforgery middleware şartı bu yüzden kapatılır.</para>
    /// </summary>
    private static void MapPhoto(RouteGroupBuilder g)
    {
        var read = g.MapGroup("/{id:guid}/fotograflar").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        read.MapGet("", PhotoList);
        read.MapGet("/{fotoId:guid}", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => PhotoContent(id, fotoId, a, ct, f.GetBytesAsync)).PhotoResponse();
        read.MapGet("/{fotoId:guid}/kucuk", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => PhotoContent(id, fotoId, a, ct, f.GetThumbAsync)).PhotoResponse();

        var write = g.MapGroup("/{id:guid}/fotograflar").RequirePermission(Permission.OperationsWrite);
        write.MapPost("", UploadPhoto).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoRequestLimit))
            .MapFields(PhotoRules);
        write.MapDelete("/{fotoId:guid}", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => PhotoOperation(id, fotoId, a, f, ct, () => f.DeleteAsync(id, fotoId, ct)));
        write.MapPost("/{fotoId:guid}/yukari", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => PhotoOperation(id, fotoId, a, f, ct, () => f.MoveAsync(id, fotoId, -1, ct)));
        write.MapPost("/{fotoId:guid}/asagi", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => PhotoOperation(id, fotoId, a, f, ct, () => f.MoveAsync(id, fotoId, 1, ct)));
    }

    private static readonly (string, string)[] PhotoRules =
    [
        ("PNG, JPEG veya WebP", "foto"), ("Fotoğraf en fazla", "foto"), ("Araç başına en fazla", "foto"), ("Fotoğraf seçilmedi", "foto"),
    ];

    private static async Task<Results<Ok<IReadOnlyList<AracFotoDto>>, ProblemHttpResult>> PhotoList(
        Guid id, VehicleService araclar, VehiclePhotoService fotolar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return NotFoundProblem();
        var list = await fotolar.ListMetaAsync(id, ct);
        return TypedResults.Ok<IReadOnlyList<AracFotoDto>>(list.Select(p => new AracFotoDto(p.Id, p.Sira, p.ContentType)).ToList());
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> PhotoContent(
        Guid id, Guid photoId, VehicleService vehicles, CancellationToken ct,
        Func<Guid, Guid, CancellationToken, Task<VehiclePhotoContent?>> fetch)
    {
        if (await vehicles.GetAsync(id, ct) is null) return F5NotFound("Araç bulunamadı.");
        var p = await fetch(id, photoId, ct);
        return p is null ? F5NotFound("Fotoğraf bulunamadı.") : TypedResults.File(p.Bytes, p.ContentType);
    }

    /// <summary>Dosya sonucu yanıt metadatası üretmiyor → OpenAPI'ye ikili görsel + 404 problem elle bildirilir.</summary>
    private static RouteHandlerBuilder PhotoResponse(this RouteHandlerBuilder b)
        => b.Produces(StatusCodes.Status200OK, typeof(byte[]), "image/png", "image/jpeg", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static ProblemHttpResult F5NotFound(string detail) => Rezervasyon.F5Shared.NotFound(detail);

    private static async Task<Results<Created<AracFotoDto>, ProblemHttpResult>> UploadPhoto(
        Guid id, IFormFile? foto, VehicleService araclar, VehiclePhotoService fotolar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return NotFoundProblem();
        if (foto is null || foto.Length == 0) throw new ValidationException("Fotoğraf seçilmedi.", "foto");
        if (foto.Length > PhotoRequestLimit) throw new ValidationException("Fotoğraf en fazla 2 MB olabilir.", "foto");
        using var ms = new MemoryStream();
        await foto.CopyToAsync(ms, ct);
        var once = (await fotolar.ListMetaAsync(id, ct)).Select(p => p.Id).ToHashSet();
        await fotolar.AddAsync(id, ms.ToArray(), ct);
        var newItem = (await fotolar.ListMetaAsync(id, ct)).FirstOrDefault(p => !once.Contains(p.Id));
        return newItem is null ? NotFoundProblem()
            : TypedResults.Created($"{Root}/{id}/fotograflar/{newItem.Id}", new AracFotoDto(newItem.Id, newItem.Sira, newItem.ContentType));
    }

    /// <summary>Sil/sırala: araç kapsamı + fotoğrafın BU araca ait olması (başka aracın foto kimliği 404).</summary>
    private static async Task<Results<Ok<IReadOnlyList<AracFotoDto>>, ProblemHttpResult>> PhotoOperation(
        Guid id, Guid photoId, VehicleService vehicles, VehiclePhotoService photos, CancellationToken ct, Func<Task> operation)
    {
        if (await vehicles.GetAsync(id, ct) is null) return NotFoundProblem();
        if ((await photos.ListMetaAsync(id, ct)).All(p => p.Id != photoId)) return F5NotFound("Fotoğraf bulunamadı.");
        await operation();
        var list = await photos.ListMetaAsync(id, ct);
        return TypedResults.Ok<IReadOnlyList<AracFotoDto>>(list.Select(p => new AracFotoDto(p.Id, p.Sira, p.ContentType)).ToList());
    }
}
