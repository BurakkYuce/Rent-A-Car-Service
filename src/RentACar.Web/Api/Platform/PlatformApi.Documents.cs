using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Platform;

namespace RentACar.Web.Api.Platform;

public static partial class PlatformApi
{
    /// <summary>Request cap: 3 MB PDF (<see cref="PdfValidation.MaxBytes"/>) + multipart/form-field overhead.</summary>
    private const long DocumentRequestLimit = 4_000_000;

    /// <summary>
    /// Document Center (Blazor <c>/platform/belgeler</c> parity): upload starts as DRAFT (tenants see nothing until
    /// published), new version replaces the file in place (tenant links keep working), archive hides without deleting.
    /// PDF-ness is decided from the bytes (<see cref="PdfValidation"/>), never from Content-Type or extension.
    /// </summary>
    private static void MapDocuments(RouteGroupBuilder g)
    {
        var d = g.MapGroup("/belgeler");
        d.MapGet("", ListDocuments);
        d.MapPost("", UploadDocument).DisableAntiforgery() // CSRF: group header filter (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(DocumentRequestLimit))
            .AlanlariEsle(DocumentRules);
        d.MapPost("/{id:guid}/surum", UploadDocumentVersion).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentRequestLimit))
            .AlanlariEsle(DocumentRules);
        d.MapPost("/{id:guid}/durum", ChangeDocumentStatus);
        d.MapDelete("/{id:guid}", DeleteDocument);
        d.MapGet("/{id:guid}/icerik", DocumentContent)
            .Produces(StatusCodes.Status200OK, typeof(byte[]), "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static readonly (string, string)[] DocumentRules =
    [
        ("Belge başlığı", "baslik"), ("Başlık", "baslik"), ("Dosya", "dosya"), ("Yalnız PDF", "dosya"),
    ];

    private static ProblemHttpResult DocumentNotFound() => F5Ortak.Bulunamadi("Belge bulunamadı.");

    private static PlatformConsoleDocumentDto ToDto(PlatformAdminService.PlatformBelgeSatiri b) => new(
        b.Id, b.Baslik, b.Aciklama, b.DosyaAdi, b.Boyut, b.Surum, DocumentStatusName(b.Durum),
        b.YalnizYoneticiler, b.GuncellemeUtc, b.YukleyenOperator, b.HedefKodlar);

    private static string DocumentStatusName(PlatformBelgeDurum s) => s switch
    {
        PlatformBelgeDurum.Yayinda => "Yayinda",
        PlatformBelgeDurum.Arsiv => "Arsiv",
        _ => "Taslak",
    };

    private static async Task<PlatformConsoleDocumentDto?> FindDocumentAsync(PlatformAdminService svc, Guid id, CancellationToken ct)
        => (await svc.ListBelgelerAsync(ct)).FirstOrDefault(b => b.Id == id) is { } b ? ToDto(b) : null;

    private static async Task<Ok<IReadOnlyList<PlatformConsoleDocumentDto>>> ListDocuments(PlatformAdminService svc, CancellationToken ct)
        => TypedResults.Ok<IReadOnlyList<PlatformConsoleDocumentDto>>((await svc.ListBelgelerAsync(ct)).Select(ToDto).ToList());

    /// <summary>Reads an uploaded PDF with the size checked BEFORE buffering (the request cap is the outer fence).</summary>
    private static async Task<byte[]> ReadPdfAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("PDF dosyası seçilmedi.", "dosya");
        if (file.Length > PdfValidation.MaxBytes)
            throw new ValidationException($"Dosya en fazla {PdfValidation.MaxBytes / (1024 * 1024)} MB olabilir.", "dosya");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// Upload (multipart): <c>baslik</c>, <c>aciklama</c>, <c>dosya</c>, <c>yalnizYoneticiler</c> (<c>true</c>/<c>false</c>),
    /// <c>hedef</c> (repeatable tenant id; none = ALL tenants). Unknown / malformed target ids are refused (400) instead
    /// of silently widening or failing on the foreign key.
    /// </summary>
    private static async Task<Results<Created<PlatformConsoleDocumentDto>, ProblemHttpResult>> UploadDocument(
        [FromForm] PlatformDocumentUploadForm form, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        var (baslik, aciklama, dosya, yalnizYoneticiler, hedef) =
            (form.Baslik, form.Aciklama, form.Dosya, form.YalnizYoneticiler, form.Hedef);
        if ((aciklama ?? "").Trim().Length > 1000)
            throw new ValidationException("Açıklama en çok 1000 karakter olabilir.", "aciklama");
        var onlyManagers = (yalnizYoneticiler ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "false" => false,
            "true" => true,
            _ => throw new ValidationException("yalnizYoneticiler true ya da false olmalıdır.", "yalnizYoneticiler"),
        };

        var targets = new List<Guid>();
        foreach (var raw in hedef ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!Guid.TryParse(raw, out var tid)) throw new ValidationException("Hedef firma kimliği geçersiz.", "hedef");
            targets.Add(tid);
        }
        if (targets.Count > 0)
        {
            var known = (await svc.ListTenantOptionsAsync(ct)).Select(o => o.Id).ToHashSet();
            if (targets.Any(t => !known.Contains(t))) throw new ValidationException("Hedef firma bulunamadı.", "hedef");
        }

        var bytes = await ReadPdfAsync(dosya, ct);
        var id = await svc.BelgeYukleAsync(baslik ?? "", aciklama, dosya!.FileName, bytes, targets, onlyManagers,
            OperatorName(http), ct);
        return await FindDocumentAsync(svc, id, ct) is { } dto
            ? TypedResults.Created($"{Root}/belgeler/{id}", dto)
            : DocumentNotFound();
    }

    private static async Task<Results<Ok<PlatformConsoleDocumentDto>, ProblemHttpResult>> UploadDocumentVersion(
        Guid id, IFormFile? dosya, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (await FindDocumentAsync(svc, id, ct) is null) return DocumentNotFound();
        var bytes = await ReadPdfAsync(dosya, ct);
        await svc.BelgeSurumGuncelleAsync(id, dosya!.FileName, bytes, OperatorName(http), ct);
        return await FindDocumentAsync(svc, id, ct) is { } dto ? TypedResults.Ok(dto) : DocumentNotFound();
    }

    /// <summary>Status: <c>Taslak</c> | <c>Yayinda</c> | <c>Arsiv</c>. Unknown value is 400 (the Blazor form silently
    /// fell back to draft — an API must not guess).</summary>
    private static async Task<Results<Ok<PlatformConsoleDocumentDto>, ProblemHttpResult>> ChangeDocumentStatus(
        Guid id, PlatformDocumentStatusRequest body, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (await FindDocumentAsync(svc, id, ct) is null) return DocumentNotFound();
        var status = (body.Durum ?? "").Trim().ToLowerInvariant() switch
        {
            "taslak" => PlatformBelgeDurum.Taslak,
            "yayinda" => PlatformBelgeDurum.Yayinda,
            "arsiv" => PlatformBelgeDurum.Arsiv,
            _ => throw new ValidationException("Durum Taslak, Yayinda ya da Arsiv olmalıdır.", "durum"),
        };
        await svc.BelgeDurumAsync(id, status, OperatorName(http), ct);
        return await FindDocumentAsync(svc, id, ct) is { } dto ? TypedResults.Ok(dto) : DocumentNotFound();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDocument(
        Guid id, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (await FindDocumentAsync(svc, id, ct) is null) return DocumentNotFound();
        await svc.BelgeSilAsync(id, OperatorName(http), ct);
        return TypedResults.NoContent();
    }

    /// <summary>Platform preview/download — no target filter here (the platform sees every document). Served with the
    /// sanitized ASCII file name (Blazor parity).</summary>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DocumentContent(
        Guid id, PlatformAdminService svc, CancellationToken ct)
        => await svc.BelgeIcerikAsync(id, ct) is { } c
            ? TypedResults.File(c.Bytes, "application/pdf", c.DosyaAdi)
            : DocumentNotFound();
}
