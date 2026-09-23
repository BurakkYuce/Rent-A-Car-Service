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

public static partial class AracApi
{
    private const int SecimEnFazla = 20;

    /// <summary>
    /// F6 seçim/typeahead uçları (<c>/araclar/secim/*</c>): Blazor ComboBox kaynaklarıyla AYNI küme — aktif tanım
    /// listesi ∪ (kapsamdaki) araç kayıtlarında geçen değerler. <c>q</c> içeren (büyük/küçük harf duyarsız), <c>limit</c>
    /// varsayılan ve en çok 20. Yalnız etiket döner (PII yok). İzin: OW VEYA ViewReports (liste süzgeçleri de kullanır).
    /// </summary>
    private static void MapSecim(RouteGroupBuilder g)
    {
        var s = g.MapGroup("/secim").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        s.MapGet("/marka", async (string? q, int? limit, BrandService m, VehicleService a, CancellationToken ct)
            => Oneri(q, limit, (await m.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.Marka)));
        s.MapGet("/tip", async (string? q, string? marka, int? limit, VehicleTypeService t, VehicleService a, CancellationToken ct) =>
        {
            var mk = Rezervasyon.F5Ortak.Nz(marka);
            bool Eslesir(string? m) => mk is null || string.Equals(m?.Trim(), mk, StringComparison.OrdinalIgnoreCase);
            return Oneri(q, limit, (await t.ListActiveAsync(ct)).Where(x => Eslesir(x.Marka)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Where(v => Eslesir(v.Marka)).Select(v => v.Tip));
        });
        s.MapGet("/renk", async (string? q, int? limit, VehicleColorService r, VehicleService a, CancellationToken ct)
            => Oneri(q, limit, (await r.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.Renk)));
        s.MapGet("/segment", async (string? q, int? limit, VehicleSegmentService sg, VehicleService a, CancellationToken ct)
            => Oneri(q, limit, (await sg.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.Segment)));
        s.MapGet("/sahip", async (string? q, int? limit, VehicleOwnerService o, VehicleService a, CancellationToken ct)
            => Oneri(q, limit, (await o.ListActiveAsync(ct)).Select(x => (x.Ad, (string?)x.Kod)),
                (await a.ListAsync(ct)).Select(v => v.AracSahibi)));
        s.MapGet("/varsayilan-grup", async (VarsayilanGrupCozucu c, CancellationToken ct)
            => TypedResults.Ok(new VarsayilanGrupDto(await c.AdAsync(ct))));
    }

    /// <summary>Tanımlılar önce (kodlu), sonra kayıtlarda geçen serbest değerler; ada göre tekil, sıralı, sınırlı.</summary>
    internal static Ok<IReadOnlyList<AracSecimDegeri>> Oneri(
        string? q, int? limit, IEnumerable<(string Ad, string? Kod)> tanimli, IEnumerable<string?> kullanilan)
    {
        var n = Math.Clamp(limit ?? SecimEnFazla, 1, SecimEnFazla);
        var ara = Rezervasyon.F5Ortak.Nz(q);
        var gorulen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sonuc = new List<AracSecimDegeri>();
        foreach (var (ad, kod) in tanimli.Where(t => !string.IsNullOrWhiteSpace(t.Ad)).OrderBy(t => t.Ad, StringComparer.Ordinal))
            if (gorulen.Add(ad.Trim())) sonuc.Add(new AracSecimDegeri(ad.Trim(), kod));
        foreach (var d in kullanilan.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).OrderBy(x => x, StringComparer.Ordinal))
            if (gorulen.Add(d)) sonuc.Add(new AracSecimDegeri(d, null));
        IEnumerable<AracSecimDegeri> suz = ara is null ? sonuc
            : sonuc.Where(x => x.Deger.Contains(ara, StringComparison.CurrentCultureIgnoreCase)
                               || (x.Kod?.Contains(ara, StringComparison.CurrentCultureIgnoreCase) ?? false));
        return TypedResults.Ok<IReadOnlyList<AracSecimDegeri>>(suz.Take(n).ToList());
    }
}
