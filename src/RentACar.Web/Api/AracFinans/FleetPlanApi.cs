using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FiloPlan;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/filo-plan/*</c> (F6.1b) — filo kapasite hedefleri + gerçekleşen sayım (<see cref="FleetPlanService"/>).
/// Para/defter TAŞIMAZ. <b>İzin:</b> okuma ViewReports ∨ OperationsWrite (servis kuralı), yazma OperationsWrite.
/// Kiracı geneli planlama (Blazor ile aynı; sayım tüm filodan). Çift oluşturma doğal anahtarla (grup/SIPP/dönem)
/// yapısal olarak engelli. Artır/Azalt satır kilidi altında (±1, kayıp güncelleme yok); PUT zorunlu <c>surum</c>.
/// </summary>
public static class FleetPlanApi
{
    private const string Root = UiApiExtensions.V1 + "/filo-plan";

    public static RouteGroupBuilder MapFleetPlanApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/filo-plan").WithTags("Filo Plan");
        g.MapGet("", GetList).MapFields(F5Shared.SortRules)
            .RequireAnyPermission(Permission.ViewReports, Permission.OperationsWrite);
        g.MapGet("/{id:guid}", Detail).RequireAnyPermission(Permission.ViewReports, Permission.OperationsWrite);
        g.MapPost("", Create).MapFields(Rules).RequirePermission(Permission.OperationsWrite);
        g.MapPut("/{id:guid}", Update).MapFields(Rules).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/delta", Delta).RequirePermission(Permission.OperationsWrite);
        g.MapDelete("/{id:guid}", Delete).RequirePermission(Permission.OperationsWrite);
        return g;
    }

    private static readonly (string, string)[] Rules =
    [
        ("Araç grubu veya SIPP", "aracGrupAdi"), ("Hedef adet", "hedefAdet"), ("Bu grup/SIPP/dönem", "donem"),
    ];

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Plan hedefi bulunamadı.");

    private static readonly SortFieldMap<FiloPlanDto> Map = SortFieldMap<FiloPlanDto>
        .Create(p => p.Id)
        .Alan("aracGrupAdi", p => p.AracGrupAdi).Alan("sipp", p => p.Sipp).Alan("donem", p => p.Donem)
        .Alan("hedefAdet", p => p.HedefAdet).Alan("gerceklesen", p => p.Gerceklesen).Alan("fark", p => p.Fark);

    private static async Task<Ok<Sayfa<FiloPlanDto>>> GetList(
        FleetPlanService svc, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
        => TypedResults.Ok(F5Shared.Paginate(
            (await svc.ListWithCountAsync(ct)).Select(s => FiloPlanDto.From(s)).ToList(), Map, sayfa, boyut, sirala));

    private static async Task<FiloPlanDto?> DtoAsync(Guid id, FleetPlanService svc, CancellationToken ct)
    {
        var version = await svc.VersionAsync(id, ct);
        var s = (await svc.ListWithCountAsync(ct)).FirstOrDefault(x => x.Hedef.Id == id);
        return s is null ? null : FiloPlanDto.From(s, version);
    }

    private static async Task<Results<Ok<FiloPlanDto>, ProblemHttpResult>> Detail(Guid id, FleetPlanService svc, CancellationToken ct)
        => await DtoAsync(id, svc, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();

    private static FiloPlanInput Input(FiloPlanIstegi i)
    {
        VehicleFinanceShared.Text(i.AracGrupAdi, 64, "aracGrupAdi");
        VehicleFinanceShared.Text(i.Sipp, 16, "sipp");
        VehicleFinanceShared.Text(i.Donem, 32, "donem");
        VehicleFinanceShared.Text(i.Aciklama, 512, "aciklama");
        if (i.HedefAdet is < 0 or > 100_000)
            throw new ValidationException("Hedef adet 0 ile 100.000 arasında olmalıdır.", "hedefAdet");
        return new FiloPlanInput
        {
            AracGrupAdi = i.AracGrupAdi, Sipp = i.Sipp, Donem = i.Donem, HedefAdet = i.HedefAdet, Aciklama = i.Aciklama,
        };
    }

    private static async Task<Results<Created<FiloPlanDto>, ProblemHttpResult>> Create(
        FiloPlanIstegi i, FleetPlanService svc, CancellationToken ct)
    {
        var id = await svc.CreateAsync(Input(i), ct);
        return await DtoAsync(id, svc, ct) is { } d ? TypedResults.Created($"{Root}/{id}", d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<FiloPlanDto>, ProblemHttpResult>> Update(
        Guid id, FiloPlanIstegi i, FleetPlanService svc, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return NotFoundProblem();
        var version = VehicleFinanceShared.Version(i.Surum);
        if (!await svc.UpdateVersionedAsync(id, Input(i), version, ct)) return NotFoundProblem();
        return await DtoAsync(id, svc, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<FiloPlanDto>, ProblemHttpResult>> Delta(
        Guid id, FiloPlanDeltaIstegi i, FleetPlanService svc, CancellationToken ct)
    {
        var delta = i.Yon?.Trim().ToLowerInvariant() switch
        {
            "artir" => 1,
            "azalt" => -1,
            _ => throw new ValidationException("Yön 'artir' ya da 'azalt' olmalıdır.", "yon"),
        };
        if (!await svc.ChangeTargetAsync(id, delta, ct)) return NotFoundProblem();
        return await DtoAsync(id, svc, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, FleetPlanService svc, CancellationToken ct)
        => await svc.DeleteAsync(id, ct) ? TypedResults.NoContent() : NotFoundProblem();
}
