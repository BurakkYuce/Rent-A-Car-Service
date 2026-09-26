using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleOwners;
using RentACar.Application.VehicleSegments;
using RentACar.Application.VehicleTypes;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Arac;

/// <summary>
/// F6.1a — araç tanımları: <c>/arac-sahipleri</c>, <c>/segmentler</c>, <c>/arac-tipleri</c> (Blazor
/// <c>VehicleOwnerList</c>/<c>VehicleSegmentList</c>/<c>VehicleTypeList</c> paritesi). Kiracı genelidir (şube alanı
/// yok). İzin OperationsWrite (menü + servis). Kod büyük harfe normalize edilir ve kiracı içinde benzersizdir.
/// PUT tam değiştirmedir: zorunlu <c>surum</c>, uyuşmazlık 409 <c>cakisma</c>.
/// </summary>
public static class VehicleDefinitionApi
{
    public static void MapVehicleDefinitionApi(this RouteGroupBuilder v1)
    {
        // ---- araç sahipleri
        var o = v1.MapGroup("/arac-sahipleri").WithTags("Araç Tanımları").RequirePermission(Permission.OperationsWrite);
        o.MapGet("", async (int? sayfa, int? boyut, string? sirala, VehicleOwnerService s, CancellationToken ct)
            => TypedResults.Ok(F5Shared.Paginate((await s.ListAsync(ct)).Select(x => new AracSahibiDto(x.Id, x.Kod, x.Ad, x.Tur, x.Aktif, null)).ToList(),
                OwnerMap, sayfa, boyut, sirala))).MapFields(F5Shared.SortRules);
        o.MapGet("/{id:guid}", async Task<Results<Ok<AracSahibiDto>, ProblemHttpResult>> (Guid id, VehicleOwnerService s, CancellationToken ct)
            => await OwnerAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : None());
        o.MapPost("", async Task<Results<Created<AracSahibiDto>, ProblemHttpResult>> (AracSahibiIstegi i, VehicleOwnerService s, CancellationToken ct) =>
        {
            OwnerLimit(i);
            var id = await s.CreateAsync(new VehicleOwnerInput { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Tur = i.Tur, Aktif = i.Aktif }, ct);
            return await OwnerAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/arac-sahipleri/{id}", d) : None();
        }).MapFields(Rules);
        o.MapPut("/{id:guid}", async Task<Results<Ok<AracSahibiDto>, ProblemHttpResult>> (Guid id, AracSahibiIstegi i, VehicleOwnerService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return None();
            VersionRequired(i.Surum);
            OwnerLimit(i);
            if (!await s.UpdateAsync(id, new VehicleOwnerInput { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Tur = i.Tur, Aktif = i.Aktif }, i.Surum, ct)) return None();
            return await OwnerAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : None();
        }).MapFields(Rules);
        o.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, VehicleOwnerService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : None());

        // ---- segmentler
        var sg = v1.MapGroup("/segmentler").WithTags("Araç Tanımları").RequirePermission(Permission.OperationsWrite);
        sg.MapGet("", async (int? sayfa, int? boyut, string? sirala, VehicleSegmentService s, CancellationToken ct)
            => TypedResults.Ok(F5Shared.Paginate((await s.ListAsync(ct)).Select(x => new SegmentDto(x.Id, x.Kod, x.Ad, x.Aciklama, x.Aktif, null)).ToList(),
                SegmentMap, sayfa, boyut, sirala))).MapFields(F5Shared.SortRules);
        sg.MapGet("/{id:guid}", async Task<Results<Ok<SegmentDto>, ProblemHttpResult>> (Guid id, VehicleSegmentService s, CancellationToken ct)
            => await SegmentAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : None());
        sg.MapPost("", async Task<Results<Created<SegmentDto>, ProblemHttpResult>> (SegmentIstegi i, VehicleSegmentService s, CancellationToken ct) =>
        {
            SegmentLimit(i);
            var id = await s.CreateAsync(new VehicleSegmentInput { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Aciklama = i.Aciklama, Aktif = i.Aktif }, ct);
            return await SegmentAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/segmentler/{id}", d) : None();
        }).MapFields(Rules);
        sg.MapPut("/{id:guid}", async Task<Results<Ok<SegmentDto>, ProblemHttpResult>> (Guid id, SegmentIstegi i, VehicleSegmentService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return None();
            VersionRequired(i.Surum);
            SegmentLimit(i);
            if (!await s.UpdateAsync(id, new VehicleSegmentInput { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Aciklama = i.Aciklama, Aktif = i.Aktif }, i.Surum, ct)) return None();
            return await SegmentAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : None();
        }).MapFields(Rules);
        sg.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, VehicleSegmentService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : None());

        // ---- araç tipleri
        var t = v1.MapGroup("/arac-tipleri").WithTags("Araç Tanımları").RequirePermission(Permission.OperationsWrite);
        t.MapGet("", async (int? sayfa, int? boyut, string? sirala, VehicleTypeService s, CancellationToken ct)
            => TypedResults.Ok(F5Shared.Paginate((await s.ListAsync(ct)).Select(x => TipDto.From(x, null)).ToList(),
                TypeMap, sayfa, boyut, sirala))).MapFields(F5Shared.SortRules);
        t.MapGet("/{id:guid}", async Task<Results<Ok<TipDto>, ProblemHttpResult>> (Guid id, VehicleTypeService s, CancellationToken ct)
            => await TipAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : None());
        t.MapPost("", async Task<Results<Created<TipDto>, ProblemHttpResult>> (TipIstegi i, VehicleTypeService s, CancellationToken ct) =>
        {
            TypeLimit(i);
            var id = await s.CreateAsync(TypeInput(i), ct);
            return await TipAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/arac-tipleri/{id}", d) : None();
        }).MapFields(Rules);
        t.MapPut("/{id:guid}", async Task<Results<Ok<TipDto>, ProblemHttpResult>> (Guid id, TipIstegi i, VehicleTypeService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return None();
            VersionRequired(i.Surum);
            TypeLimit(i);
            if (!await s.UpdateAsync(id, TypeInput(i), i.Surum, ct)) return None();
            return await TipAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : None();
        }).MapFields(Rules);
        t.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, VehicleTypeService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : None());
    }

    private static ProblemHttpResult None() => F5Shared.NotFound("Kayıt bulunamadı.");

    private static void VersionRequired(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
    }

    /// <summary>Servis mesajı → alan (kod zorunlu/uzunluk/benzersiz, ad zorunlu).</summary>
    private static readonly (string, string)[] Rules =
    [
        ("Araç sahibi kodu", "kod"), ("Segment kodu", "kod"), ("Araç tipi kodu", "kod"), ("'", "kod"),
        ("Araç sahibi adı", "ad"), ("Segment adı", "ad"), ("Araç tipi adı", "ad"),
    ];

    private static void OwnerLimit(AracSahibiIstegi i)
    {
        RentalLimits.Text(i.Ad, 128, "ad", "Ad");
        RentalLimits.Text(i.Tur, 32, "tur", "Tür");
    }

    private static void SegmentLimit(SegmentIstegi i)
    {
        RentalLimits.Text(i.Ad, 128, "ad", "Ad");
        RentalLimits.Text(i.Aciklama, 512, "aciklama", "Açıklama");
    }

    private static void TypeLimit(TipIstegi i)
    {
        RentalLimits.Text(i.Ad, 128, "ad", "Ad");
        RentalLimits.Text(i.Marka, 64, "marka", "Marka");
        RentalLimits.Text(i.Vites, 32, "vites", "Vites");
        RentalLimits.Text(i.Yakit, 32, "yakit", "Yakıt");
        RentalLimits.Text(i.Grup, 64, "grup", "Grup");
    }

    private static VehicleTypeInput TypeInput(TipIstegi i) => new()
    { Kod = i.Kod ?? "", Ad = i.Ad ?? "", Marka = i.Marka, Vites = i.Vites, Yakit = i.Yakit, Grup = i.Grup, Aktif = i.Aktif };

    /// <summary>Sürüm alanlardan ÖNCE okunur.</summary>
    private static async Task<AracSahibiDto?> OwnerAsync(Guid id, VehicleOwnerService s, CancellationToken ct)
    {
        var version = await s.VersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? new AracSahibiDto(x.Id, x.Kod, x.Ad, x.Tur, x.Aktif, version) : null;
    }

    private static async Task<SegmentDto?> SegmentAsync(Guid id, VehicleSegmentService s, CancellationToken ct)
    {
        var version = await s.VersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? new SegmentDto(x.Id, x.Kod, x.Ad, x.Aciklama, x.Aktif, version) : null;
    }

    private static async Task<TipDto?> TipAsync(Guid id, VehicleTypeService s, CancellationToken ct)
    {
        var version = await s.VersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? TipDto.From(x, version) : null;
    }

    private static readonly SortFieldMap<AracSahibiDto> OwnerMap = SortFieldMap<AracSahibiDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("tur", x => x.Tur).Alan("aktif", x => x.Aktif);
    private static readonly SortFieldMap<SegmentDto> SegmentMap = SortFieldMap<SegmentDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("aktif", x => x.Aktif);
    private static readonly SortFieldMap<TipDto> TypeMap = SortFieldMap<TipDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("marka", x => x.Marka)
        .Alan("grup", x => x.Grup).Alan("aktif", x => x.Aktif);
}

/// <summary>Araç sahibi. <c>Surum</c> yalnız tekil yanıtta dolu.</summary>
public sealed record AracSahibiDto(Guid Id, string Kod, string Ad, string? Tur, bool Aktif, string? Surum);
/// <summary>POST/PUT gövdesi; PUT'ta <c>surum</c> ZORUNLU.</summary>
public sealed record AracSahibiIstegi(string? Kod, string? Ad, string? Tur, bool Aktif = true, string? Surum = null);

public sealed record SegmentDto(Guid Id, string Kod, string Ad, string? Aciklama, bool Aktif, string? Surum);
public sealed record SegmentIstegi(string? Kod, string? Ad, string? Aciklama, bool Aktif = true, string? Surum = null);

public sealed record TipDto(Guid Id, string Kod, string Ad, string? Marka, string? Vites, string? Yakit, string? Grup, bool Aktif, string? Surum)
{
    public static TipDto From(Domain.Entities.VehicleType x, string? version)
        => new(x.Id, x.Kod, x.Ad, x.Marka, x.Vites, x.Yakit, x.Grup, x.Aktif, version);
}
public sealed record TipIstegi(string? Kod, string? Ad, string? Marka, string? Vites, string? Yakit, string? Grup,
    bool Aktif = true, string? Surum = null);
