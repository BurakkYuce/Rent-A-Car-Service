using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FirmaDokumanlar;
using RentACar.Application.PlatformBelgeler;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — company documents (<c>/dokumanlar</c>, Blazor <c>Dokumanlar</c>) and platform documents shared with the
/// company (<c>/firma-belgeleri</c>, Blazor <c>FirmaBelgeleri</c>).
/// <list type="bullet">
/// <item>Lists: any signed-in user (Blazor pages are <c>[Authorize]</c> only — field staff incl. accounting print
/// them) → <c>IzinMuaf</c> with reason. Platform documents keep their per-document manager-only flag (service).</item>
/// <item>Upload/delete: OperationsWrite (Blazor group + service guard). PDF by magic bytes, 3 MB, 10 per tenant —
/// all in the service.</item>
/// <item>No NEW file endpoint: rows carry <c>indirmeYolu</c>, the existing cookie-authenticated download route
/// (ETag, 404 for another tenant, never a redirect for a file).</item>
/// </list>
/// </summary>
public static class DocumentApi
{
    private const string Tag = "Dokümanlar";
    private const string ReadReason = "Blazor sayfası yalnız [Authorize]: oturum açmış her personel (muhasebe dahil) belgeyi görür/indirir.";

    /// <summary>Service limit + multipart envelope (same as the Blazor upload).</summary>
    private const long RequestLimit = CompanyFileService.MaxBytes + 1024 * 1024;

    public static void MapDocumentApi(this RouteGroupBuilder v1)
    {
        var docs = v1.MapGroup("/dokumanlar").WithTags(Tag);
        docs.MapGet("", async Task<Ok<IReadOnlyList<CompanyDocumentDto>>> (CompanyFileService s, CancellationToken ct)
            => TypedResults.Ok<IReadOnlyList<CompanyDocumentDto>>((await s.ListAsync(ct)).Select(CompanyDocumentDto.From).ToList()))
            .IzinMuaf(ReadReason);

        docs.MapPost("", async Task<Results<Created<CompanyDocumentDto>, ProblemHttpResult>> (
                IFormFile? dosya, [FromForm] string? baslik, [FromForm] string? aciklama, CompanyFileService s, CancellationToken ct) =>
            {
                if (dosya is null || dosya.Length == 0) throw new ValidationException("Dosya seçilmedi.", "dosya");
                if (dosya.Length > CompanyFileService.MaxBytes)
                    throw new ValidationException($"Dosya en fazla {CompanyFileService.MaxBytes / (1024 * 1024)} MB olabilir.", "dosya");
                Sinirlar.Metin(baslik, 200, "baslik", "Başlık");
                Sinirlar.Metin(aciklama, 1000, "aciklama", "Açıklama");
                using var ms = new MemoryStream();
                await dosya.CopyToAsync(ms, ct);
                var id = await s.UploadAsync(new FirmaDokumanInput(baslik, aciklama, dosya.FileName, ms.ToArray()), ct);
                return (await s.ListAsync(ct)).FirstOrDefault(x => x.Id == id) is { } row
                    ? TypedResults.Created($"{UiApiExtensions.V1}/dokumanlar/{id}", CompanyDocumentDto.From(row))
                    : F5Ortak.Bulunamadi("Doküman bulunamadı.");
            })
            .RequirePermission(Permission.OperationsWrite)
            .DisableAntiforgery() // CSRF: the group's X-XSRF-TOKEN header filter (form binding's own check is off)
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimit))
            .AlanlariEsle([("Başlık", "baslik"), ("Dosya", "dosya"), ("Yalnız PDF", "dosya"), ("PDF", "dosya"), ("En fazla", "dosya")]);

        docs.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, CompanyFileService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : F5Ortak.Bulunamadi("Doküman bulunamadı."))
            .RequirePermission(Permission.OperationsWrite);

        v1.MapGet("/firma-belgeleri", async Task<Ok<IReadOnlyList<PlatformDocumentDto>>> (PlatformDocumentService s, CancellationToken ct)
            => TypedResults.Ok<IReadOnlyList<PlatformDocumentDto>>((await s.ListAsync(ct)).Select(PlatformDocumentDto.From).ToList()))
            .WithTags(Tag).IzinMuaf(ReadReason);
    }
}

/// <summary>Company PDF. <c>indirmeYolu</c>: cookie-authenticated download (always attachment).</summary>
public sealed record CompanyDocumentDto(Guid Id, string Baslik, string? Aciklama, string DosyaAdi, long Boyut, int Sira,
    string? YukleyenKullanici, DateTimeOffset Olusturma, string IndirmeYolu)
{
    public static CompanyDocumentDto From(FirmaDokumanSatiri r) => new(r.Id, r.Baslik, r.Aciklama, r.DosyaAdi, r.Boyut, r.Sira,
        r.YukleyenKullanici, r.CreatedAtUtc, $"/dokumanlar/{r.Id}/indir");
}

/// <summary>Platform document visible to this company. <c>indirmeYolu</c> opens inline; add <c>?indir=1</c> to save.</summary>
public sealed record PlatformDocumentDto(Guid Id, string Baslik, string? Aciklama, int Surum, DateTimeOffset Guncelleme,
    long Boyut, bool Yeni, string IndirmeYolu)
{
    public static PlatformDocumentDto From(FirmaBelgeSatiri r)
        => new(r.Id, r.Baslik, r.Aciklama, r.Surum, r.Guncelleme, r.Boyut, r.Yeni, $"/firma-belgeleri/{r.Id}/indir");
}
