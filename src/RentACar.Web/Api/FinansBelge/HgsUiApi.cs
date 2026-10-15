using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Hgs;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using RentACar.Web.Import;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// <c>/api/ui/v1/hgs/*</c> — HGS geçişlerini dosyadan içe aktarma (API'siz ilk aşama), liste, yansıtma.
/// <list type="bullet">
/// <item><b>İzin:</b> okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (ceza ile aynı); içe aktarma ve yeniden eşleştirme
/// OperationsWrite ∨ FinanceWrite + şube KISITSIZ (servis zorlar: HGS dökümü kiracı genelidir); yansıtma (defter yazar)
/// FinanceWrite.</item>
/// <item><b>Kapsam:</b> geçişin kirası, yoksa aracı; ikisi de yoksa (plaka filoda yok) kiracı geneli. Başka şube 403,
/// olmayan/başka kiracı 404; kapsam durumdan ÖNCE.</item>
/// <item><b>İçe aktarma iki adım:</b> <c>/ice-aktar/onizle</c> hiçbir şey yazmaz; <c>/ice-aktar</c> aynı dosya + aynı
/// eşlemeyle yazar. Mükerrer geçiş DB'de (doğal anahtar unique index'i) elenir — iki kez göndermek tek kayıt üretir.</item>
/// <item><b>Yansıtma</b> mevcut HGS deseni: Borç Cari / Alacak Gelir, <c>SourceType = "Hgs"</c>, <c>SourceId</c> = geçiş;
/// tekrar gönderim yeni para yazmaz (zaten yansıtılmış satır atlanır, defter kısmi unique index'i son savunma).</item>
/// </list>
/// </summary>
public static class HgsUiApi
{
    /// <summary>Yükleme sınırı (tarife aktar ile aynı).</summary>
    private const long UploadLimit = 5 * 1024 * 1024;
    private const int MaxMappingChars = 4096;

    private static readonly (string, string)[] ReflectRules =
    [
        ("Yansıtılacak geçiş", "ids"), ("Tek seferde", "ids"), ("Hizmet oranı", "hizmetOrani"),
    ];

    public static RouteGroupBuilder MapHgsUiApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/hgs").WithTags("HGS");
        g.MapGet("/gecisler", List).MapFields(SortRules)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("/ice-aktar/onizle", Preview).DisableAntiforgery() // CSRF: grup başlık süzgeci (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(UploadLimit))
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);
        g.MapPost("/ice-aktar", Import).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadLimit))
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);
        g.MapPost("/gecisler/yansit", Reflect).MapFields(ReflectRules).RequirePermission(Permission.FinanceWrite)
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/gecisler/yeniden-eslestir", Rematch)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);
        return g;
    }

    // ================================================================== liste

    private static readonly SortFieldMap<HgsCrossingRow> Sort = SortFieldMap<HgsCrossingRow>
        .Create(r => r.Id)
        .Alan("gecisZamani", r => r.GecisZamani).Alan("plaka", r => r.Plaka).Alan("tutar", r => r.Tutar)
        .Alan("durum", r => r.Durum).Alan("sozlesmeNo", r => r.SozlesmeNo).Alan("cariAd", r => r.CariAd);

    public sealed class HgsListFilter
    {
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        /// <summary>İstanbul günü (dahil).</summary>
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        /// <summary>İstanbul günü (dahil).</summary>
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
        /// <summary>Eslesti | KiraDisi | Belirsiz | Yansitildi.</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "kiraId")] public Guid? KiraId { get; set; }
        [FromQuery(Name = "iceAktarimId")] public Guid? IceAktarimId { get; set; }
    }

    private static async Task<Ok<Sayfa<HgsCrossingRow>>> List(
        [AsParameters] HgsListFilter f, HgsCrossingService svc, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        Text(f.Plaka, 16, "plaka");
        var rows = await svc.ListAsync(new HgsCrossingFilter
        {
            Plaka = F5Shared.Nz(f.Plaka),
            Bas = f.Bas is { } b ? F5Shared.DayStart(b, "bas") : null,
            Bit = f.Bit is { } t ? F5Shared.DayStart(t, "bit").AddDays(1) : null,
            Durum = F5Shared.EnumAdi<HgsCrossingStatus>(f.Durum, "durum"),
            RentalId = f.KiraId, IceAktarimId = f.IceAktarimId,
        }, ct);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var branches = await BranchesAsync(db, rows, ct);
        var visible = rows.Where(c => InScope(user, branches[c.Id])).ToList();
        return TypedResults.Ok(F5Shared.Paginate(await RowsAsync(db, dbf, visible, ct), Sort, sayfa, boyut, sirala));
    }

    /// <summary>Kira formu "Ceza/HGS" sekmesi ve liste için satır zenginleştirme (sözleşme no, cari adı).</summary>
    internal static async Task<IReadOnlyList<HgsCrossingRow>> RowsAsync(
        AppDbContext db, IDbContextFactory<AppDbContext> dbf, IReadOnlyList<HgsCrossing> list, CancellationToken ct)
    {
        var rentalIds = list.Where(c => c.RentalId is not null).Select(c => c.RentalId!.Value).Distinct().ToList();
        var rentals = await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.Id))
            .Select(r => new { r.Id, r.SozlesmeNo, r.MusteriId }).ToDictionaryAsync(r => r.Id, ct);
        Guid? CustomerOf(HgsCrossing c) => c.YansitilanCariId
            ?? (c.RentalId is { } r && rentals.TryGetValue(r, out var x) ? x.MusteriId : null);
        var names = await F5Shared.CustomersAsync(dbf, list.Select(CustomerOf).OfType<Guid>(), ct);
        return list.Select(c =>
        {
            var customer = CustomerOf(c);
            return new HgsCrossingRow(
                c.Id, c.Plaka, c.VehicleId, c.GecisZamani, c.GirisZamani, c.GirisIstasyonu, c.CikisIstasyonu, c.Tutar,
                c.Durum.ToString(), c.RentalId,
                c.RentalId is { } r && rentals.TryGetValue(r, out var x) ? x.SozlesmeNo : null,
                customer, customer is { } id ? F5Shared.CustomerName(names, id) : null,
                c.HizmetOrani, c.YansitilanTutar, c.YansitmaZamani, c.EtiketNo, c.AracSinifi, c.DosyaAdi);
        }).ToList();
    }

    /// <summary>Geçiş → şube: kirası, yoksa aracı; ikisi de yoksa bilinmiyor (yalnız kısıtsız kullanıcı görür).</summary>
    private static async Task<Dictionary<Guid, BranchInfo>> BranchesAsync(
        AppDbContext db, IReadOnlyList<HgsCrossing> list, CancellationToken ct)
    {
        var rentals = await RentalBranchesAsync(db, list.Where(c => c.RentalId is not null).Select(c => c.RentalId!.Value), ct);
        var vehicles = await VehicleBranchesAsync(db, list.Where(c => c.RentalId is null && c.VehicleId is not null)
            .Select(c => c.VehicleId!.Value), ct);
        return list.ToDictionary(c => c.Id, c =>
            c.RentalId is { } r ? rentals.GetValueOrDefault(r)
            : c.VehicleId is { } v ? vehicles.GetValueOrDefault(v) : default);
    }

    // ================================================================== içe aktarma

    private static Task<Ok<HgsImportResultDto>> Preview(
        IFormFile? dosya, [FromForm(Name = "eslesme")] string? eslesme, HgsCrossingService svc, CancellationToken ct)
        => RunAsync(dosya, eslesme, svc, write: false, ct);

    private static Task<Ok<HgsImportResultDto>> Import(
        IFormFile? dosya, [FromForm(Name = "eslesme")] string? eslesme, HgsCrossingService svc, CancellationToken ct)
        => RunAsync(dosya, eslesme, svc, write: true, ct);

    private static async Task<Ok<HgsImportResultDto>> RunAsync(
        IFormFile? file, string? mappingJson, HgsCrossingService svc, bool write, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("Dosya seçilmedi.", "dosya");
        if (file.Length > UploadLimit) throw new ValidationException("Dosya en fazla 5 MB olabilir.", "dosya");
        var name = Path.GetFileName(file.FileName ?? "");
        if (!(name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Yalnız .xlsx ya da .csv yüklenebilir (.xls dosyasını Excel'de .xlsx olarak kaydedin).", "dosya");
        var mapping = ParseMapping(mappingJson);

        ParsedImport parsed;
        try
        {
            await using var s = file.OpenReadStream();
            parsed = ImportService.ParseWithHeaders(s, name, new ImportParseOptions(InvariantCells: true, Windows1254Fallback: true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ValidationException)
        {
            throw new ValidationException("Dosya okunamadı (biçim bozuk ya da desteklenmiyor).", "dosya");
        }
        if (parsed.Headers.Count == 0 || parsed.Rows.Count == 0)
            throw new ValidationException("Dosyada başlık satırı ve en az bir geçiş satırı olmalı.", "dosya");

        var (labels, keys) = HgsImportColumns.Resolve(parsed.Headers, mapping);
        var fields = HgsImportColumns.Fields
            .Select(fd => new HgsFieldDto(fd.Kod, fd.Etiket, fd.Zorunlu, labels.GetValueOrDefault(fd.Kod))).ToList();
        var headers = parsed.Headers.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();

        // Önizleme zorunlu alan eksikken de döner (kullanıcı eşlemeyi ekranda tamamlasın); yazma döndürmez.
        var missing = HgsImportColumns.Fields.Any(fd => fd.Zorunlu && !keys.ContainsKey(fd.Kod));
        if (missing && write) HgsImportColumns.RequireMandatory(keys);
        if (missing)
            return TypedResults.Ok(new HgsImportResultDto(headers, fields, parsed.Rows.Count, 0, 0, 0, 0, 0, 0, 0, 0m, 0m,
                ["Zorunlu alanlar (plaka, geçiş zamanı, tutar) için dosyadaki sütunu seçin."], [], null));

        var raw = HgsImportColumns.Extract(parsed.Rows, keys);
        var outcome = write ? await svc.ImportAsync(raw, name, ct) : await svc.PreviewAsync(raw, ct);
        return TypedResults.Ok(new HgsImportResultDto(
            headers, fields, outcome.Toplam, outcome.Eslesen, outcome.KiraDisi, outcome.Belirsiz, outcome.AracBulunamadi,
            outcome.Mukerrer, outcome.Hatali, outcome.Eklenen, outcome.EslesenTutar, outcome.KiraDisiTutar, outcome.Uyarilar,
            outcome.Satirlar.Select(r => new HgsImportRowDto(r.SatirNo, r.Plaka, r.GecisZamani, r.GirisIstasyonu,
                r.CikisIstasyonu, r.Tutar, r.Durum.ToString(), r.RentalId, r.SozlesmeNo, r.AracBulunamadi, r.Hata)).ToList(),
            outcome.IceAktarimId));
    }

    /// <summary>Eşleme JSON'u (<c>{"plaka":"Araç Plakası", …}</c>); boş → sunucu önerisi.</summary>
    private static Dictionary<string, string?>? ParseMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.Length > MaxMappingChars) throw new ValidationException("Eşleme çok uzun.", "eslesme");
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                ?? throw new ValidationException("Eşleme okunamadı.", "eslesme");
        }
        catch (JsonException)
        {
            throw new ValidationException("Eşleme okunamadı.", "eslesme");
        }
    }

    // ================================================================== yansıtma (PARA)

    private static async Task<Results<Ok<HgsReflectResultDto>, ProblemHttpResult>> Reflect(
        HgsReflectRequest req, HgsCrossingService svc, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        var ids = (req.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("Yansıtılacak geçiş seçilmedi.", "ids");
        if (ids.Count > HgsCrossingService.MaxReflectIds)
            throw new ValidationException($"Tek seferde en çok {HgsCrossingService.MaxReflectIds} geçiş yansıtılabilir.", "ids");
        var rate = req.HizmetOrani ?? throw new ValidationException("Hizmet oranı zorunludur (1,00 = bedelsiz).", "hizmetOrani");

        // Kapsam durumdan ÖNCE: olmayan/başka kiracı 404, başka şube 403.
        var found = await svc.FindManyAsync(ids, ct);
        if (found.Count != ids.Count) return F5Shared.NotFound("Geçiş bulunamadı.");
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            var branches = await BranchesAsync(db, found, ct);
            foreach (var c in found) RequireInScope(user, branches[c.Id]);
        }
        var r = await svc.ReflectAsync(ids, rate, ct);
        return TypedResults.Ok(new HgsReflectResultDto(r.Yansitilan, r.ZatenYansitilmis, r.Uygunsuz, r.ToplamGecis, r.YansitilanTutar));
    }

    private static async Task<Ok<HgsRematchResult>> Rematch(HgsCrossingService svc, CancellationToken ct)
        => TypedResults.Ok(new HgsRematchResult(await svc.RematchAsync(ct)));
}
