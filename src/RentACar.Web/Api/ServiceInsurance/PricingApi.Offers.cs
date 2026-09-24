using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// <c>/maliyet-teklifleri</c> — kaydedilmiş filo maliyet teklifi (planlama belgesi; DEFTERE YAZMAZ). Sonuç alanları
/// istemciden ALINMAZ: servis girdiden yeniden hesaplar ve snapshot'ı yazar. Cari adı KVKK kuralıyla
/// (<see cref="Kira.MusteriGorunumu"/>) döner. Oluşturmada <c>Idempotency-Key</c> isteğe bağlı → kayıt Id'si.
/// </summary>
internal static partial class PricingApi
{
    private const string OffersRoot = UiApiExtensions.V1 + "/maliyet-teklifleri";

    private static void MapCostOffers(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/maliyet-teklifleri").WithTags("Fiyat & Tarife");
        g.MapGet("", ListOffers).AlanlariEsle(F5Ortak.SiralamaKurallari)
            .RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", OfferDetail).RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", CreateOffer).AlanlariEsle(CostFieldRules("girdi.")).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPut("/{id:guid}", UpdateOffer).AlanlariEsle(CostFieldRules("girdi.")).RequirePermission(Permission.FinanceWrite);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, MaliyetTeklifiService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : OfferNotFound()).RequirePermission(Permission.FinanceWrite);
    }

    private static ProblemHttpResult OfferNotFound() => S.NotFound("Maliyet teklifi bulunamadı.");

    private static readonly SiralamaHaritasi<CostOfferRow> OfferSort = SiralamaHaritasi<CostOfferRow>
        .Olustur(x => x.Id).Alan("kayitNo", x => x.KayitNo).Alan("baslik", x => x.Baslik).Alan("tarih", x => x.Tarih)
        .Alan("plaka", x => x.Plaka).Alan("teklifAylikNet", x => x.TeklifAylikNet).Alan("filoTeklifKdvli", x => x.FiloTeklifKdvli);

    private static async Task<List<CostOfferRow>> OfferRowsAsync(IDbContextFactory<AppDbContext> dbf, IReadOnlyList<MaliyetTeklifi> list,
        CancellationToken ct)
    {
        var names = await S.CustomersAsync(dbf, list.Select(t => t.CariId), ct);
        return list.Select(t => new CostOfferRow(t.Id, t.KayitNo, t.Baslik, t.Plaka, t.Tarih, t.CariId, S.CustomerName(names, t.CariId),
            t.HazirlayanId, t.AracSayisi, t.TeklifAylikNet, t.TeklifKdvli, t.FiloTeklifAylikNet, t.FiloTeklifKdvli)).ToList();
    }

    private static async Task<Ok<CostOfferList>> ListOffers(
        string? metin, string? plaka, Guid? cariId, DateOnly? bas, DateOnly? bit, decimal? fiyatMin, decimal? fiyatMax,
        int? sayfa, int? boyut, string? sirala, MaliyetTeklifiService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var list = await svc.SearchAsync(new MaliyetTeklifiFilter
        {
            Metin = F5Ortak.Nz(metin), Plaka = F5Ortak.Nz(plaka), CariId = cariId, TarihMin = min, TarihMax = max,
            FiyatMin = fiyatMin, FiyatMax = fiyatMax,
        }, ct);
        var rows = await OfferRowsAsync(dbf, list, ct);
        return TypedResults.Ok(new CostOfferList(F5Ortak.Sayfala(rows, OfferSort, sayfa, boyut, sirala), MaliyetTeklifiService.Ozet(list)));
    }

    private static async Task<Results<Ok<CostOfferDetail>, ProblemHttpResult>> OfferDetail(
        Guid id, MaliyetTeklifiService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await OfferDetailAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : OfferNotFound();

    private static async Task<CostOfferDetail?> OfferDetailAsync(Guid id, MaliyetTeklifiService svc, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        var version = await svc.GetVersionAsync(id, ct); // BEFORE the fields
        if (await svc.GetAsync(id, ct) is not { } t) return null;
        var row = (await OfferRowsAsync(dbf, [t], ct))[0];
        return new CostOfferDetail(row, t.Aciklama, CostInputDto.From(MaliyetTeklifiService.GirdiyeCevir(t)),
            CostResultDto.From(t, MaliyetTeklifiService.Kalemler(t)), version);
    }

    private static async Task<MaliyetTeklifiInput> OfferInputAsync(CostOfferRequest r, IDbContextFactory<AppDbContext> dbf,
        Guid? id, CancellationToken ct)
    {
        S.Text(r.Baslik, 256, "baslik"); S.Text(r.Plaka, 32, "plaka"); S.Text(r.Aciklama, 1024, "aciklama");
        var input = CostInput(r.Girdi ?? new CostInputDto(null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null, null), "girdi.");
        await AracFinansOrtak.CariVarAsync(dbf, r.CariId, "cariId", zorunlu: false, ct);
        if (r.HazirlayanId is { } p && p != Guid.Empty)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            if (!await db.Personeller.AsNoTracking().AnyAsync(x => x.Id == p, ct))
                throw new ValidationException("Personel bulunamadı.", "hazirlayanId");
        }
        return new MaliyetTeklifiInput
        {
            Id = id, Baslik = r.Baslik, Plaka = r.Plaka, Tarih = S.Date(r.Tarih, "tarih"),
            CariId = r.CariId is { } c && c != Guid.Empty ? c : null,
            HazirlayanId = r.HazirlayanId is { } h && h != Guid.Empty ? h : null, Aciklama = r.Aciklama, Girdi = input,
        };
    }

    private static async Task<Results<Created<CostOfferDetail>, ProblemHttpResult>> CreateOffer(
        CostOfferRequest r, HttpContext http, MaliyetTeklifiService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var key = IdempotencyBasligi.Anahtar(http);
        if (key is { } k && await svc.GetAsync(k, ct) is { } m) throw OfferDuplicate(m, r); // (1) ÖNCE mevcut
        var input = await OfferInputAsync(r, dbf, key, ct);
        Guid id;
        try
        {
            id = await svc.CreateAsync(input, ct);
        }
        catch (DbUpdateException ex) when (key is { } k2 && S.IsPrimaryKeyViolation(ex))
        {
            if (await svc.GetAsync(k2, ct) is { } won) throw OfferDuplicate(won, r);
            throw new MukerrerIslemException(RegulationApi.AnahtarBaskaIslemde);
        }
        var d = await OfferDetailAsync(id, svc, dbf, ct);
        return TypedResults.Created($"{OffersRoot}/{id}", d!);
    }

    private static MukerrerIslemException OfferDuplicate(MaliyetTeklifi m, CostOfferRequest r)
        => new($"Bu teklif zaten kaydedildi ({m.KayitNo}); yeni kayıt yazılmadı.",
            new MevcutIslem(m.Id, m.KayitNo, m.FiloTeklifKdvli, "TRY",
                m.Baslik == (r.Baslik ?? "").Trim() && m.AlisBedeli == (r.Girdi?.AlisBedeli ?? 0m)
                && m.AracSayisi == (r.Girdi?.AracSayisi ?? 1)));

    private static async Task<Results<Ok<CostOfferDetail>, ProblemHttpResult>> UpdateOffer(
        Guid id, CostOfferRequest r, MaliyetTeklifiService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return OfferNotFound();
        var version = AracFinansOrtak.Surum(r.Surum);
        var input = await OfferInputAsync(r, dbf, null, ct);
        if (!await svc.UpdateVersionedAsync(id, input, version, ct)) return OfferNotFound();
        return TypedResults.Ok((await OfferDetailAsync(id, svc, dbf, ct))!);
    }
}
