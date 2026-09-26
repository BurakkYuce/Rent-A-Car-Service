using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.InsuranceCompanies;
using RentACar.Application.KdvRates;
using RentACar.Application.PenaltyTypes;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>Sigorta şirketleri, KDV oranları, ceza türleri — Kod+Ad(+tek alan) tanımları. OperationsWrite.</summary>
public static partial class SystemDefinitionsApi
{
    // ------------------------------------------------------------------ sigorta şirketleri

    private static readonly (string, string)[] InsuranceRules =
        [("Sigorta şirketi kodu", "kod"), ("Sigorta şirketi adı", "ad"), ("'", "kod")];

    private static readonly SortFieldMap<InsuranceCompanyDto> InsuranceSort = SortFieldMap<InsuranceCompanyDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("aktif", x => x.Aktif);

    private static void MapInsuranceCompanies(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/sigorta-sirketleri").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.OperationsWrite);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? ara, bool? aktif, InsuranceCompanyService s, CancellationToken ct)
            => TypedResults.Ok(F5Shared.Paginate(
                Filter((await s.ListAsync(ct)).Select(x => InsuranceCompanyDto.From(x, null)), ara, aktif, x => [x.Kod, x.Ad, x.Telefon], x => x.Aktif),
                InsuranceSort, sayfa, boyut, sirala))).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", async Task<Results<Ok<InsuranceCompanyDto>, ProblemHttpResult>> (Guid id, InsuranceCompanyService s, CancellationToken ct)
            => await InsuranceAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<InsuranceCompanyDto>, ProblemHttpResult>> (InsuranceCompanyRequest i, InsuranceCompanyService s, CancellationToken ct) =>
        {
            InsuranceLimits(i);
            var id = await s.CreateAsync(InsuranceInput(i), ct);
            return await InsuranceAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/sigorta-sirketleri/{id}", d) : SystemApiCommon.NotFound();
        }).MapFields(InsuranceRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<InsuranceCompanyDto>, ProblemHttpResult>> (Guid id, InsuranceCompanyRequest i, InsuranceCompanyService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            InsuranceLimits(i);
            if (!await s.UpdateAsync(id, InsuranceInput(i), i.Surum, ct)) return SystemApiCommon.NotFound();
            return await InsuranceAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).MapFields(InsuranceRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, InsuranceCompanyService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound());
    }

    private static void InsuranceLimits(InsuranceCompanyRequest i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        Text(i.Telefon, 32, "telefon", "Telefon");
    }

    private static InsuranceCompanyInput InsuranceInput(InsuranceCompanyRequest i)
        => new() { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Telefon = i.Telefon, Aktif = i.Aktif };

    /// <summary>Sürüm alanlardan ÖNCE okunur (arada yazım olursa bayat sürüm 409 üretir, bayat veri değil).</summary>
    private static async Task<InsuranceCompanyDto?> InsuranceAsync(Guid id, InsuranceCompanyService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? InsuranceCompanyDto.From(x, version) : null;
    }

    // ------------------------------------------------------------------ KDV oranları

    private static readonly (string, string)[] VatRules =
        [("KDV oranı kodu", "kod"), ("KDV oranı adı", "ad"), ("KDV oranı 0 ile 1", "oran"), ("'", "kod")];

    private static readonly SortFieldMap<KdvRateDto> VatSort = SortFieldMap<KdvRateDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("oran", x => x.Oran).Alan("aktif", x => x.Aktif);

    private static void MapVatRates(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/kdv-oranlari").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.OperationsWrite);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? ara, bool? aktif, VatRateService s, CancellationToken ct)
            => TypedResults.Ok(F5Shared.Paginate(
                Filter((await s.ListAsync(ct)).Select(x => KdvRateDto.From(x, null)), ara, aktif, x => [x.Kod, x.Ad], x => x.Aktif),
                VatSort, sayfa, boyut, sirala))).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", async Task<Results<Ok<KdvRateDto>, ProblemHttpResult>> (Guid id, VatRateService s, CancellationToken ct)
            => await VatAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<KdvRateDto>, ProblemHttpResult>> (KdvRateRequest i, VatRateService s, CancellationToken ct) =>
        {
            VatLimits(i);
            var id = await s.CreateAsync(VatInput(i), ct);
            return await VatAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/kdv-oranlari/{id}", d) : SystemApiCommon.NotFound();
        }).MapFields(VatRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<KdvRateDto>, ProblemHttpResult>> (Guid id, KdvRateRequest i, VatRateService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            VatLimits(i);
            if (!await s.UpdateAsync(id, VatInput(i), i.Surum, ct)) return SystemApiCommon.NotFound();
            return await VatAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).MapFields(VatRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, VatRateService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound());
    }

    private static void VatLimits(KdvRateRequest i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        if (i.Oran is null) throw new ValidationException("KDV oranı zorunludur (0.20 = %20).", "oran");
    }

    private static KdvRateInput VatInput(KdvRateRequest i)
        => new() { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Oran = i.Oran ?? 0m, Aktif = i.Aktif };

    private static async Task<KdvRateDto?> VatAsync(Guid id, VatRateService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? KdvRateDto.From(x, version) : null;
    }

    // ------------------------------------------------------------------ ceza türleri

    private static readonly (string, string)[] PenaltyRules =
        [("Ceza türü kodu", "kod"), ("Ceza türü adı", "ad"), ("Varsayılan tutar", "varsayilanTutar"), ("'", "kod")];

    private static readonly SortFieldMap<PenaltyTypeDto> PenaltySort = SortFieldMap<PenaltyTypeDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("varsayilanTutar", x => x.VarsayilanTutar)
        .Alan("aktif", x => x.Aktif);

    private static void MapPenaltyTypes(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ceza-turleri").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.OperationsWrite);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? ara, bool? aktif, PenaltyTypeService s, CancellationToken ct)
            => TypedResults.Ok(F5Shared.Paginate(
                Filter((await s.ListAsync(ct)).Select(x => PenaltyTypeDto.From(x, null)), ara, aktif, x => [x.Kod, x.Ad], x => x.Aktif),
                PenaltySort, sayfa, boyut, sirala))).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", async Task<Results<Ok<PenaltyTypeDto>, ProblemHttpResult>> (Guid id, PenaltyTypeService s, CancellationToken ct)
            => await PenaltyAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<PenaltyTypeDto>, ProblemHttpResult>> (PenaltyTypeRequest i, PenaltyTypeService s, CancellationToken ct) =>
        {
            PenaltyLimits(i);
            var id = await s.CreateAsync(PenaltyInput(i), ct);
            return await PenaltyAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/ceza-turleri/{id}", d) : SystemApiCommon.NotFound();
        }).MapFields(PenaltyRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<PenaltyTypeDto>, ProblemHttpResult>> (Guid id, PenaltyTypeRequest i, PenaltyTypeService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            PenaltyLimits(i);
            if (!await s.UpdateAsync(id, PenaltyInput(i), i.Surum, ct)) return SystemApiCommon.NotFound();
            return await PenaltyAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).MapFields(PenaltyRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, PenaltyTypeService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound());
    }

    private static void PenaltyLimits(PenaltyTypeRequest i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        Amount(i.VarsayilanTutar, "varsayilanTutar", "Varsayılan tutar");
    }

    private static PenaltyTypeInput PenaltyInput(PenaltyTypeRequest i)
        => new() { Kod = i.Kod ?? "", Ad = i.Ad ?? "", VarsayilanTutar = i.VarsayilanTutar, Aktif = i.Aktif };

    private static async Task<PenaltyTypeDto?> PenaltyAsync(Guid id, PenaltyTypeService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? PenaltyTypeDto.From(x, version) : null;
    }
}
