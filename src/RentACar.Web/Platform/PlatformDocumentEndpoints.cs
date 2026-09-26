using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Web.Identity;

namespace RentACar.Web.Platform;

/// <summary>
/// PR-B — Belge Merkezi'nin PLATFORM uçları (yükle/sürüm/durum/sil/indir). Tamamı
/// <see cref="PlatformClaims.Policy"/> ardında: tenant personeli buraya HİÇ erişemez.
/// Tenant tarafı ayrı ve SALT-OKUR (<c>/firma-belgeleri</c>).
/// </summary>
public static class PlatformDocumentEndpoints
{
    public static IEndpointRouteBuilder MapPlatformDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/platform/belgeler")
            .RequireAuthorization(PlatformClaims.Policy)
            .AntiforgeryByEnv();

        // Yükle → TASLAK olarak (tenant görmez). Hedef seçilmemişse GLOBAL.
        grp.MapPost("/yukle", async (HttpContext http, PlatformAdminService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var file = f.Files["dosya"];
            try
            {
                if (file is null || file.Length == 0)
                    throw new ValidationException("PDF dosyası seçilmedi.");
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);

                var targets = f["hedef"]
                    .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty).ToList();

                await svc.UploadDocumentAsync(
                    f["baslik"].ToString(), FormParse.Str(f, "aciklama"), file.FileName, ms.ToArray(),
                    targets, f["yalnizYoneticiler"].ToString() == "true",
                    http.User.Identity?.Name ?? "platform");
                return Results.Redirect("/platform/belgeler?ok=1");
            }
            catch (ValidationException ex) { return Back(ex); }
        }).WithMetadata(new RequestSizeLimitAttribute(11_000_000)); // 10 MB + multipart payı

        // Sürüm güncelle: yeni kayıt AÇILMAZ, dosya değişir + Surum++ → tenant'ın linki kırılmaz.
        grp.MapPost("/{id:guid}/surum", async (HttpContext http, PlatformAdminService svc, Guid id, HttpRequest req) =>
        {
            var file = req.Form.Files["dosya"];
            try
            {
                if (file is null || file.Length == 0) throw new ValidationException("PDF dosyası seçilmedi.");
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                await svc.UpdateDocumentVersionAsync(id, file.FileName, ms.ToArray(), http.User.Identity?.Name ?? "platform");
                return Results.Redirect("/platform/belgeler?ok=1");
            }
            catch (ValidationException ex) { return Back(ex); }
        }).WithMetadata(new RequestSizeLimitAttribute(11_000_000));

        grp.MapPost("/{id:guid}/durum", async (HttpContext http, PlatformAdminService svc, Guid id, [FromForm] string durum) =>
        {
            try
            {
                var newItem = durum switch
                {
                    "yayinda" => PlatformBelgeDurum.Yayinda,
                    "arsiv" => PlatformBelgeDurum.Arsiv,
                    _ => PlatformBelgeDurum.Taslak,
                };
                await svc.DocumentStatusAsync(id, newItem, http.User.Identity?.Name ?? "platform");
                return Results.Redirect("/platform/belgeler?ok=1");
            }
            catch (ValidationException ex) { return Back(ex); }
        });

        grp.MapPost("/{id:guid}/sil", async (HttpContext http, PlatformAdminService svc, Guid id) =>
        {
            try
            {
                await svc.DeleteDocumentAsync(id, http.User.Identity?.Name ?? "platform");
                return Results.Redirect("/platform/belgeler?ok=1");
            }
            catch (ValidationException ex) { return Back(ex); }
        });

        // Platform operatörünün önizlemesi — hedef filtresi UYGULANMAZ (burada her belgeyi görme yetkisi var).
        grp.MapGet("/{id:guid}/indir", async (PlatformAdminService svc, Guid id, CancellationToken ct) =>
        {
            var content = await svc.DocumentContentAsync(id, ct);
            return content is null
                ? Results.NotFound()
                : Results.File(content.Value.Bytes, "application/pdf", content.Value.DosyaAdi);
        });

        return app;
    }

    private static IResult Back(ValidationException ex)
        => Results.Redirect("/platform/belgeler?hata=" + Uri.EscapeDataString(ex.Message));
}
