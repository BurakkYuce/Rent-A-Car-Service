using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Brands;
using RentACar.Application.VehicleColors;
using RentACar.Application.VehicleGroups;
using RentACar.Application.VehicleOwners;
using RentACar.Application.VehicleSegments;
using RentACar.Application.Vehicles;
using RentACar.Application.VehicleTypes;
using RentACar.Domain.Entities;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Arac;

/// <summary>Seç-veya-yaz önerisi: tanımlı değer (<c>Kod</c> dolu) ya da kayıtlarda geçen serbest değer (<c>Kod</c> null).</summary>
public sealed record AracSecimDegeri(string Deger, string? Kod);

/// <summary>Firmanın varsayılan araç grubu (yeni araç formunda önseçili; yoksa null).</summary>
public sealed record VarsayilanGrupDto(string? Ad);

public static partial class VehicleApi
{
    private const int SelectionMax = 20;

    /// <summary>
    /// F6 seçim/typeahead uçları (<c>/araclar/secim/*</c>): Blazor ComboBox kaynaklarıyla AYNI küme — aktif tanım
    /// listesi ∪ (kapsamdaki) araç kayıtlarında geçen değerler. <c>q</c> içeren (büyük/küçük harf duyarsız), <c>limit</c>
    /// varsayılan ve en çok 20. Yalnız etiket döner (PII yok). İzin: OW VEYA ViewReports (liste süzgeçleri de kullanır).
    /// </summary>
    private static void MapSelection(RouteGroupBuilder g)
    {
        var s = g.MapGroup("/secim").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        s.MapGet("/marka", async (string? q, int? limit, BrandService m, VehicleService a, CancellationToken ct)
            => Suggestion(q, limit, (await m.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.Marka)));
        s.MapGet("/tip", async (string? q, string? marka, int? limit, VehicleTypeService t, VehicleService a, CancellationToken ct) =>
        {
            var mk = Rezervasyon.F5Shared.Nz(marka);
            bool Matches(string? m) => mk is null || string.Equals(m?.Trim(), mk, StringComparison.OrdinalIgnoreCase);
            return Suggestion(q, limit, (await t.ListActiveAsync(ct)).Where(x => Matches(x.Marka)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Where(v => Matches(v.Marka)).Select(v => v.Tip));
        });
        s.MapGet("/renk", async (string? q, int? limit, VehicleColorService r, VehicleService a, CancellationToken ct)
            => Suggestion(q, limit, (await r.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.Renk)));
        s.MapGet("/segment", async (string? q, int? limit, VehicleSegmentService sg, VehicleService a, CancellationToken ct)
            => Suggestion(q, limit, (await sg.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.Segment)));
        s.MapGet("/sahip", async (string? q, int? limit, VehicleOwnerService o, VehicleService a, CancellationToken ct)
            => Suggestion(q, limit, (await o.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.AracSahibi)));
        s.MapGet("/varsayilan-grup", async (DefaultGroupResolver c, CancellationToken ct)
            => TypedResults.Ok(new VarsayilanGrupDto(await c.NameAsync(ct))));
    }

    /// <summary>Tanımlılar önce (kodlu), sonra kayıtlarda geçen serbest değerler; ada göre tekil, sıralı, sınırlı.</summary>
    internal static Ok<IReadOnlyList<AracSecimDegeri>> Suggestion(
        string? q, int? limit, IEnumerable<(string Ad, string? Kod)> defined, IEnumerable<string?> used)
    {
        var n = Math.Clamp(limit ?? SelectionMax, 1, SelectionMax);
        var search = Rezervasyon.F5Shared.Nz(q);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AracSecimDegeri>();
        foreach (var (name, code) in defined.Where(t => !string.IsNullOrWhiteSpace(t.Ad)).OrderBy(t => t.Ad, StringComparer.Ordinal))
            if (seen.Add(name.Trim())) result.Add(new AracSecimDegeri(name.Trim(), code));
        foreach (var d in used.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).OrderBy(x => x, StringComparer.Ordinal))
            if (seen.Add(d)) result.Add(new AracSecimDegeri(d, null));
        IEnumerable<AracSecimDegeri> filter = search is null ? result
            : result.Where(x => x.Deger.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                               || (x.Kod?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false));
        return TypedResults.Ok<IReadOnlyList<AracSecimDegeri>>(filter.Take(n).ToList());
    }
}
