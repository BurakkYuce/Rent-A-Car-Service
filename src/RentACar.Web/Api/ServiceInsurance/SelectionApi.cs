using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.InsuranceCompanies;
using RentACar.Application.Secim;
using RentACar.Application.TarifeGruplari;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — fazın kendi seçim/typeahead uçları (<c>/secim/*</c>, F1.6 sözleşmesi: <c>q</c> + <c>limit</c> ≤ 20, yalnız kimlik +
/// etiket + kod). Araç/cari/şube/araç grubu/sigorta ürünü/rezervasyon kaynağı seçimleri önceki fazlarda açıldı.
/// <list type="bullet">
/// <item><c>sigorta-sirketi</c> — poliçe formu firma listesi (aktif tanımlar).</item>
/// <item><c>tarife-grubu</c> — tarife formu (aktif gruplar; şifre/kullanıcı adı DÖNMEZ).</item>
/// <item><c>sigorta-policesi</c> — zeyil formu; ARACIN şubesi kapsamında (şubeli kullanıcı başka şubenin poliçesini görmez).</item>
/// </list>
/// </summary>
internal static class SelectionApi
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/secim").WithTags("Seçim");
        g.MapGet("/sigorta-sirketi", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, InsuranceCompanyService s,
                CancellationToken ct)
            => TypedResults.Ok(Filter((await s.ListActiveAsync(ct)).Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)), q, limit)))
            .RequirePermission(Permission.OperationsWrite);
        g.MapGet("/tarife-grubu", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, TariffGroupService s,
                CancellationToken ct)
            => TypedResults.Ok(Filter((await s.ListActiveAsync(ct)).Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)), q, limit)))
            .RequirePermission(Permission.OperationsWrite);
        g.MapGet("/sigorta-policesi", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit,
                IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct) =>
            {
                await using var db = await dbf.CreateDbContextAsync(ct);
                var policies = await db.InsurancePolicies.AsNoTracking().OrderByDescending(p => p.Bitis).ToListAsync(ct);
                var visible = await S.VisibleAsync(dbf, user, policies, p => p.VehicleId, ct);
                var plates = await S.PlatesAsync(dbf, visible.Select(p => p.VehicleId), ct);
                return TypedResults.Ok(Filter(visible.Select(p => new SecimOgesi(p.Id,
                    $"{F5Ortak.Plaka(plates, p.VehicleId)} · {p.Tip}" + (string.IsNullOrWhiteSpace(p.PoliceNo) ? "" : $" · {p.PoliceNo}"),
                    p.PoliceNo)), q, limit));
            }).RequirePermission(Permission.OperationsWrite);
    }

    private static IReadOnlyList<SecimOgesi> Filter(IEnumerable<SecimOgesi> items, string? q, int? limit)
    {
        var n = Math.Clamp(limit ?? SelectionService.MaxLimit, 1, SelectionService.MaxLimit);
        var text = F5Ortak.Nz(q);
        return items.Where(i => text is null || i.Etiket.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                                || (i.Kod?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .Take(n).ToList();
    }
}
