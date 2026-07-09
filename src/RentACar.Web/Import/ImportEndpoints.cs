using RentACar.Application.Authorization;
using RentACar.Web.Identity;

namespace RentACar.Web.Import;

/// <summary>Veri göçü içe-aktarım uçları (Excel/CSV → araç / cari). ManageUsers-gate'li (PII toplu-yazımı,
/// Admin). Dosya IFormFile ile alınır; sonuç sayaçları query-string ile /ice-aktar sayfasına döner.</summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ice-aktar").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/arac", async (IFormFile? dosya, ImportService imp, CancellationToken ct) =>
        {
            if (dosya is null || dosya.Length == 0) return Results.Redirect("/ice-aktar?tur=arac&mesaj=Dosya%20se%C3%A7ilmedi");
            await using var s = dosya.OpenReadStream();
            var rows = ImportService.Parse(s, dosya.FileName);
            var r = await imp.ImportAraclarAsync(rows, ct);
            return Results.Redirect($"/ice-aktar?tur=arac&eklenen={r.Eklenen}&atlanan={r.Atlanan}&hatali={r.Hatali}");
        });

        grp.MapPost("/cari", async (IFormFile? dosya, ImportService imp, CancellationToken ct) =>
        {
            if (dosya is null || dosya.Length == 0) return Results.Redirect("/ice-aktar?tur=cari&mesaj=Dosya%20se%C3%A7ilmedi");
            await using var s = dosya.OpenReadStream();
            var rows = ImportService.Parse(s, dosya.FileName);
            var r = await imp.ImportCarilerAsync(rows, ct);
            return Results.Redirect($"/ice-aktar?tur=cari&eklenen={r.Eklenen}&atlanan={r.Atlanan}&hatali={r.Hatali}");
        });

        return app;
    }
}
