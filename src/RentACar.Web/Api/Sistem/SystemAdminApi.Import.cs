using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;
using RentACar.Web.Import;

namespace RentACar.Web.Api.Sistem;

/// <summary>Veri göçü sonucu: sayaçlar + hatalı satırların mesaj özeti (satır etiketi YOK — KVKK).</summary>
public sealed record ImportCountsDto(int Eklenen, int Atlanan, int Hatali, IReadOnlyList<ImportErrorSummaryDto> HataOzeti);

/// <summary>Aynı hata mesajının kaç satırda görüldüğü.</summary>
public sealed record ImportErrorSummaryDto(string Mesaj, int Adet);

/// <summary>
/// F11.2d — Veri İçe Aktar (Blazor <c>/ice-aktar/arac</c>, <c>/ice-aktar/cari</c>): Excel/CSV → araç / cari. ManageUsers
/// (Blazor uç politikası; toplu PII yazımı). Müşteri TC/ehliyet/pasaport servis yolundan ŞİFRELİ girer; tekrar eden plaka/TC
/// atlanır (satır satır, atomik değil — Blazor ile aynı). CSRF: grup X-XSRF-TOKEN filtresi (form bağlamanın kendi denetimi
/// kapalı). Yanıt satır etiketi (müşteri adı/ünvanı) TAŞIMAZ: hatalar yalnız mesaj + adet olarak özetlenir.
/// </summary>
public static partial class SystemAdminApi
{
    /// <summary>Dosya üst sınırı 5 MB (tarife aktarımıyla aynı) + multipart payı.</summary>
    private const long ImportFileLimit = 5 * 1024 * 1024;
    private const long ImportRequestLimit = ImportFileLimit + 1024 * 1024;
    private const int ImportMaxRows = 20_000;

    private static readonly (string, string)[] ImportRules = [("Dosya", "dosya"), ("Yalnız", "dosya"), ("Tek seferde", "dosya")];

    private static void MapImport(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ice-aktar").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);
        g.MapPost("/arac", async Task<Ok<ImportCountsDto>> (IFormFile? dosya, ImportService imp, CancellationToken ct)
                => TypedResults.Ok(Summary(await imp.ImportAraclarAsync(await ReadRowsAsync(dosya), ct))))
            .DisableAntiforgery() // CSRF: group header filter (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(ImportRequestLimit))
            .AlanlariEsle(ImportRules);
        g.MapPost("/cari", async Task<Ok<ImportCountsDto>> (IFormFile? dosya, ImportService imp, CancellationToken ct)
                => TypedResults.Ok(Summary(await imp.ImportCarilerAsync(await ReadRowsAsync(dosya), ct))))
            .DisableAntiforgery() // CSRF: group header filter (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(ImportRequestLimit))
            .AlanlariEsle(ImportRules);
    }

    private static async Task<IReadOnlyList<Dictionary<string, string>>> ReadRowsAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) throw new ValidationException("Dosya seçilmedi.", "dosya");
        if (file.Length > ImportFileLimit) throw new ValidationException("Dosya en fazla 5 MB olabilir.", "dosya");
        var name = file.FileName ?? "";
        if (!(name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
              || name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Yalnız .xlsx, .xls ya da .csv dosyası yüklenebilir.", "dosya");
        IReadOnlyList<Dictionary<string, string>> rows;
        try
        {
            await using var s = file.OpenReadStream();
            rows = ImportService.Parse(s, name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ValidationException)
        {
            throw new ValidationException("Dosya okunamadı (biçim bozuk ya da desteklenmiyor).", "dosya");
        }
        if (rows.Count > ImportMaxRows) throw new ValidationException("Tek seferde en çok 20.000 satır aktarılabilir.", "dosya");
        return rows;
    }

    /// <summary>Servis hataları "etiket: mesaj" biçiminde (etiket = plaka ya da müşteri adı). Etiket atılır, aynı mesajlar
    /// sayılır — yanıt kişisel veri taşımaz. Sayaç (<c>Hatali</c>) servisin gerçek hata sayısıdır (özet kısaltılmış olabilir).</summary>
    private static ImportCountsDto Summary(ImportResult r) => new(
        r.Eklenen, r.Atlanan, r.Hatali,
        r.Hatalar
            .Select(h => h.IndexOf(": ", StringComparison.Ordinal) is var i and >= 0 ? h[(i + 2)..] : h)
            .GroupBy(m => m)
            .Select(x => new ImportErrorSummaryDto(x.Key, x.Count()))
            .OrderByDescending(x => x.Adet)
            .Take(20)
            .ToList());
}
