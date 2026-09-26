using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Application.Regulation;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>Tüm poliçelerin zeyilleri tek listede (#301; Blazor "Zeyil (Poliçe Ekleri)" tablosu). Zeyil bilgi kaydıdır
/// (deftere yazmaz). Poliçe etiketi: plaka + poliçe no + firma.</summary>
public sealed record EndorsementListRow(Guid Id, Guid PolicyId, Guid VehicleId, string Plaka, string? PoliceNo, string? Firma,
    string PoliceTipi, string ZeyilNo, DateTimeOffset Tarih, DateTimeOffset? Tanzim, decimal Deger, decimal Brut, decimal Net,
    decimal FonVergi, string? Tipi, string? Neden);

internal static partial class RegulationApi
{
    private static readonly SortFieldMap<EndorsementListRow> EndorsementSort = SortFieldMap<EndorsementListRow>
        .Create(x => x.Id).Alan("plaka", x => x.Plaka).Alan("zeyilNo", x => x.ZeyilNo).Alan("tarih", x => x.Tarih)
        .Alan("deger", x => x.Deger).Alan("brut", x => x.Brut).Alan("net", x => x.Net).Alan("tipi", x => x.Tipi);

    /// <summary>
    /// <c>GET /regulasyon/zeyiller</c>: kapsam poliçenin ARACININ şubesi (poliçe listesiyle aynı kural; başka şubenin zeyili
    /// satır olarak hiç görünmez). Süzgeçler: plaka (içerir), tipi (birebir, büyük/küçük harf duyarsız), zeyil tarihi aralığı.
    /// </summary>
    private static async Task<Ok<Sayfa<EndorsementListRow>>> ListEndorsements(
        string? plaka, string? tipi, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut, string? sirala,
        RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        S.Text(plaka, 32, "plaka");
        S.Text(tipi, 64, "tipi");
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var policies = await S.VisibleAsync(dbf, user, await reg.ListInsuranceAsync(ct), p => p.VehicleId, ct);
        var byId = policies.ToDictionary(p => p.Id);
        var plates = await S.PlatesAsync(dbf, policies.Select(p => p.VehicleId), ct);
        var rows = (await reg.ListAllEndorsementsAsync(ct))
            .Where(z => byId.ContainsKey(z.PolicyId) && (min is null || z.Tarih >= min) && (max is null || z.Tarih <= max)
                        && (S.Nz(tipi) is not { } t || string.Equals(z.Tipi?.Trim(), t, StringComparison.OrdinalIgnoreCase)))
            .Select(z =>
            {
                var p = byId[z.PolicyId];
                return new EndorsementListRow(z.Id, z.PolicyId, p.VehicleId, F5Ortak.Plaka(plates, p.VehicleId), p.PoliceNo,
                    p.Firma, p.Tip.ToString(), z.ZeyilNo, z.Tarih, z.Tanzim, z.Deger, z.Brut, z.Net, z.FonVergi, z.Tipi, z.Neden);
            })
            .Where(r => S.Nz(plaka) is not { } q || r.Plaka.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(rows, EndorsementSort, sayfa, boyut, sirala));
    }
}
