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

    private static readonly (string, string)[] ImportRules = [("Dosya", "dosya"), ("Yalnız", "dosya"), ("Tek seferde", "dosya"), ("Başlık", "dosya"), ("Bir satır", "dosya")];

    private static void MapImport(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ice-aktar").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);
        g.MapPost("/arac", async Task<Ok<ImportCountsDto>> (IFormFile? dosya, ImportService imp, CancellationToken ct)
                => TypedResults.Ok(ImportSummary(await imp.ImportAraclarAsync(await ReadRowsAsync(dosya), ct))))
            .DisableAntiforgery() // CSRF: group header filter (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(ImportRequestLimit))
            .AlanlariEsle(ImportRules);
        g.MapPost("/cari", async Task<Ok<ImportCountsDto>> (IFormFile? dosya, ImportService imp, CancellationToken ct)
                => TypedResults.Ok(ImportSummary(await imp.ImportCarilerAsync(await ReadRowsAsync(dosya), ct))))
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
        return rows; // satır/sütun/boyut sınırları ayrıştırma SIRASINDA (ImportLimits)
    }

    /// <summary>
    /// Yalnız MESAJ kullanılır (<see cref="ImportError.Message"/>); satır etiketi (plaka / müşteri adı) yapısal olarak ayrı
    /// taşındığı için metin ayrıştırmaya gerek yok ve etiket hiçbir biçimde yanıta girmez (#308 L1). Aynı mesajlar sayılır;
    /// en sık 20 mesajdan sonrası tek "Diğer hatalar" satırında toplanır → Σ adet = <c>Hatali</c> her zaman.
    /// </summary>
    /// <remarks>SAF ve <c>public</c>: doğrudan test edilebilsin diye (repoda <c>InternalsVisibleTo</c> yok).</remarks>
    public static ImportCountsDto ImportSummary(ImportResult r)
    {
        var groups = r.Errors.GroupBy(e => e.Message)
            .Select(x => new ImportErrorSummaryDto(x.Key, x.Count()))
            .OrderByDescending(x => x.Adet).ThenBy(x => x.Mesaj, StringComparer.Ordinal)
            .ToList();
        var summary = groups.Take(ImportSummaryMax).ToList();
        var rest = groups.Skip(ImportSummaryMax).Sum(x => x.Adet);
        if (rest > 0) summary.Add(new ImportErrorSummaryDto("Diğer hatalar", rest));
        return new ImportCountsDto(r.Eklenen, r.Atlanan, r.Hatali, summary);
    }

    private const int ImportSummaryMax = 20;
}
