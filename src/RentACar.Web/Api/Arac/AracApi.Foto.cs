using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Arac;

public static partial class AracApi
{
    /// <summary>İstek gövdesi üst sınırı: 2 MB fotoğraf + multipart payı (Blazor ucuyla aynı).</summary>
    private const long FotoIstekSiniri = 3_000_000;

    /// <summary>
    /// Fotoğraf galerisi (Blazor <c>VehicleEdit</c> galeri bölümü). Okuma: OW VEYA ViewReports; yazma: OW. Her uç ÖNCE
    /// aracın varlığı (404) ve şube kapsamından (403) geçer; foto başka araca aitse 404. Tür (PNG/JPEG/WebP, içerikten
    /// tespit), 2 MB ve araç başına 20 adet sınırı servistedir.
    /// <para>Yükleme <c>multipart/form-data</c> (<c>foto</c> alanı). CSRF grubun başlık filtresinde doğrulanır
    /// (<c>X-XSRF-TOKEN</c>); form bağlamanın ayrı antiforgery middleware şartı bu yüzden kapatılır.</para>
    /// </summary>
    private static void MapFoto(RouteGroupBuilder g)
    {
        var oku = g.MapGroup("/{id:guid}/fotograflar").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        oku.MapGet("", FotoListe);
        oku.MapGet("/{fotoId:guid}", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => FotoIcerik(id, fotoId, a, ct, f.GetBytesAsync)).FotoYaniti();
        oku.MapGet("/{fotoId:guid}/kucuk", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => FotoIcerik(id, fotoId, a, ct, f.GetThumbAsync)).FotoYaniti();

        var yaz = g.MapGroup("/{id:guid}/fotograflar").RequirePermission(Permission.OperationsWrite);
        yaz.MapPost("", FotoYukle).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(FotoIstekSiniri))
            .AlanlariEsle(FotoKurallari);
        yaz.MapDelete("/{fotoId:guid}", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => FotoIslem(id, fotoId, a, f, ct, () => f.DeleteAsync(id, fotoId, ct)));
        yaz.MapPost("/{fotoId:guid}/yukari", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => FotoIslem(id, fotoId, a, f, ct, () => f.MoveAsync(id, fotoId, -1, ct)));
        yaz.MapPost("/{fotoId:guid}/asagi", (Guid id, Guid fotoId, VehicleService a, VehiclePhotoService f, CancellationToken ct)
            => FotoIslem(id, fotoId, a, f, ct, () => f.MoveAsync(id, fotoId, 1, ct)));
    }

    private static readonly (string, string)[] FotoKurallari =
    [
        ("PNG, JPEG veya WebP", "foto"), ("Fotoğraf en fazla", "foto"), ("Araç başına en fazla", "foto"), ("Fotoğraf seçilmedi", "foto"),
    ];

    private static async Task<Results<Ok<IReadOnlyList<AracFotoDto>>, ProblemHttpResult>> FotoListe(
        Guid id, VehicleService araclar, VehiclePhotoService fotolar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        var liste = await fotolar.ListMetaAsync(id, ct);
        return TypedResults.Ok<IReadOnlyList<AracFotoDto>>(liste.Select(p => new AracFotoDto(p.Id, p.Sira, p.ContentType)).ToList());
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> FotoIcerik(
        Guid id, Guid fotoId, VehicleService araclar, CancellationToken ct,
        Func<Guid, Guid, CancellationToken, Task<VehiclePhotoContent?>> getir)
    {
        if (await araclar.GetAsync(id, ct) is null) return F5Bulunamadi("Araç bulunamadı.");
        var p = await getir(id, fotoId, ct);
        return p is null ? F5Bulunamadi("Fotoğraf bulunamadı.") : TypedResults.File(p.Bytes, p.ContentType);
    }

    /// <summary>Dosya sonucu yanıt metadatası üretmiyor → OpenAPI'ye ikili görsel + 404 problem elle bildirilir.</summary>
    private static RouteHandlerBuilder FotoYaniti(this RouteHandlerBuilder b)
        => b.Produces(StatusCodes.Status200OK, typeof(byte[]), "image/png", "image/jpeg", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static ProblemHttpResult F5Bulunamadi(string detay) => Rezervasyon.F5Ortak.Bulunamadi(detay);

    private static async Task<Results<Created<AracFotoDto>, ProblemHttpResult>> FotoYukle(
        Guid id, IFormFile? foto, VehicleService araclar, VehiclePhotoService fotolar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        if (foto is null || foto.Length == 0) throw new ValidationException("Fotoğraf seçilmedi.", "foto");
        if (foto.Length > FotoIstekSiniri) throw new ValidationException("Fotoğraf en fazla 2 MB olabilir.", "foto");
        using var ms = new MemoryStream();
        await foto.CopyToAsync(ms, ct);
        var once = (await fotolar.ListMetaAsync(id, ct)).Select(p => p.Id).ToHashSet();
        await fotolar.AddAsync(id, ms.ToArray(), ct);
        var yeni = (await fotolar.ListMetaAsync(id, ct)).FirstOrDefault(p => !once.Contains(p.Id));
        return yeni is null ? Bulunamadi()
            : TypedResults.Created($"{Kok}/{id}/fotograflar/{yeni.Id}", new AracFotoDto(yeni.Id, yeni.Sira, yeni.ContentType));
    }

    /// <summary>Sil/sırala: araç kapsamı + fotoğrafın BU araca ait olması (başka aracın foto kimliği 404).</summary>
    private static async Task<Results<Ok<IReadOnlyList<AracFotoDto>>, ProblemHttpResult>> FotoIslem(
        Guid id, Guid fotoId, VehicleService araclar, VehiclePhotoService fotolar, CancellationToken ct, Func<Task> islem)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        if ((await fotolar.ListMetaAsync(id, ct)).All(p => p.Id != fotoId)) return F5Bulunamadi("Fotoğraf bulunamadı.");
        await islem();
        var liste = await fotolar.ListMetaAsync(id, ct);
        return TypedResults.Ok<IReadOnlyList<AracFotoDto>>(liste.Select(p => new AracFotoDto(p.Id, p.Sira, p.ContentType)).ToList());
    }
}
