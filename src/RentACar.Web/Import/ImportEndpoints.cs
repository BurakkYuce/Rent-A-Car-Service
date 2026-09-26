using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Import;

/// <summary>Veri göçü içe-aktarım uçları (Excel/CSV → araç / cari). ManageUsers-gate'li (PII toplu-yazımı,
/// Admin). Dosya IFormFile ile alınır; sonuç sayaçları query-string ile /ice-aktar sayfasına döner.</summary>
public static class ImportEndpoints
{
    /// <summary>F11.2d (H1): ayrıştırma sınırı ihlali (<see cref="ImportLimits"/>) sayfaya mesajla döner (500/hata sayfası
    /// değil). Dönüş: hata mesajı ya da <c>null</c>.</summary>
    private static string? ParseFile(IFormFile file, out IReadOnlyList<Dictionary<string, string>> rows)
    {
        try
        {
            using var s = file.OpenReadStream();
            rows = ImportService.Parse(s, file.FileName);
            return null;
        }
        catch (ValidationException ex)
        {
            rows = [];
            return ex.Message;
        }
    }

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ice-aktar").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/arac", async (IFormFile? dosya, ImportService imp, CancellationToken ct) =>
        {
            if (dosya is null || dosya.Length == 0) return Results.Redirect("/ice-aktar?tur=arac&mesaj=Dosya%20se%C3%A7ilmedi");
            if (ParseFile(dosya, out var rows) is { } err) return Results.Redirect($"/ice-aktar?tur=arac&mesaj={Uri.EscapeDataString(err)}");
            var r = await imp.ImportAraclarAsync(rows, ct);
            return Sonuc.Tamam($"/ice-aktar?tur=arac&eklenen={r.Eklenen}&atlanan={r.Atlanan}&hatali={r.Hatali}", "Araçlar içe aktarıldı.");
        });

        grp.MapPost("/cari", async (IFormFile? dosya, ImportService imp, CancellationToken ct) =>
        {
            if (dosya is null || dosya.Length == 0) return Results.Redirect("/ice-aktar?tur=cari&mesaj=Dosya%20se%C3%A7ilmedi");
            if (ParseFile(dosya, out var rows) is { } err) return Results.Redirect($"/ice-aktar?tur=cari&mesaj={Uri.EscapeDataString(err)}");
            var r = await imp.ImportCarilerAsync(rows, ct);
            return Sonuc.Tamam($"/ice-aktar?tur=cari&eklenen={r.Eklenen}&atlanan={r.Atlanan}&hatali={r.Hatali}", "Cariler içe aktarıldı.");
        });

        // FAZ 6.1 — toplu tarife aktarımı (xml_fiyat_aktar karşılığı). Aynı gate (Admin/ManageUsers):
        // toplu fiyat yazımı yapılandırma-hassas; satırlar Beklemede girer (onay akışı ImportService'te çitli).
        var tarife = app.MapGroup("/tarife-aktar").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        tarife.MapPost("/yukle", async (IFormFile? dosya, ImportService imp, CancellationToken ct) =>
        {
            if (dosya is null || dosya.Length == 0) return Results.Redirect("/tarife-aktar?mesaj=Dosya%20se%C3%A7ilmedi");
            if (ParseFile(dosya, out var rows) is { } err) return Results.Redirect($"/tarife-aktar?mesaj={Uri.EscapeDataString(err)}");
            var r = await imp.ImportTarifelerAsync(rows, ct);
            var hatalar = r.Hatalar.Count == 0
                ? ""
                : "&hatalar=" + Uri.EscapeDataString(string.Join("|", r.Hatalar.Take(5)));
            return Sonuc.Tamam($"/tarife-aktar?eklenen={r.Eklenen}&atlanan={r.Atlanan}&hatali={r.Hatali}{hatalar}", "Dosya yüklendi.");
        });

        // FAZ-31 — bir rezervasyon kaynağının BEKLEYEN tarife satırlarını toplu sil. Onaylı
        // tarifeler servis çitiyle korunuyor (fiyat motoru yalnız onaylıyı kullanır) — uç bu
        // kararı gevşetemesin diye durum parametresi DIŞARIDAN alınmaz, sabit gönderilir.
        tarife.MapPost("/kanal-sil", async (RateMatrixService svc, HttpRequest req) =>
        {
            // Form olmayan bir POST'ta req.Form InvalidOperationException atar → 500 (adversarial L1).
            if (!req.HasFormContentType) return Results.BadRequest();

            var kanal = FormParse.Str(req.Form, "kanal");
            var geri = $"/tarife-aktar?kanal={Uri.EscapeDataString(kanal ?? "")}";
            try
            {
                var n = await svc.DeleteByChannelAsync(kanal, TariffApprovalStatus.Bekliyor);
                return Results.Redirect($"{geri}&mesaj={Uri.EscapeDataString($"{n} bekleyen tarife satırı silindi.")}");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"{geri}&mesaj={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
