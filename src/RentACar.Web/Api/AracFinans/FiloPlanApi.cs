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
public static class FiloPlanApi
{
    private const string Kok = UiApiExtensions.V1 + "/filo-plan";

    public static RouteGroupBuilder MapFiloPlanApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/filo-plan").WithTags("Filo Plan");
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari)
            .RequireAnyPermission(Permission.ViewReports, Permission.OperationsWrite);
        g.MapGet("/{id:guid}", Detay).RequireAnyPermission(Permission.ViewReports, Permission.OperationsWrite);
        g.MapPost("", Olustur).AlanlariEsle(Kurallar).RequirePermission(Permission.OperationsWrite);
        g.MapPut("/{id:guid}", Guncelle).AlanlariEsle(Kurallar).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/delta", Delta).RequirePermission(Permission.OperationsWrite);
        g.MapDelete("/{id:guid}", Sil).RequirePermission(Permission.OperationsWrite);
        return g;
    }

    private static readonly (string, string)[] Kurallar =
    [
        ("Araç grubu veya SIPP", "aracGrupAdi"), ("Hedef adet", "hedefAdet"), ("Bu grup/SIPP/dönem", "donem"),
    ];

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Plan hedefi bulunamadı.");

    private static readonly SortFieldMap<FiloPlanDto> Harita = SortFieldMap<FiloPlanDto>
        .Create(p => p.Id)
        .Alan("aracGrupAdi", p => p.AracGrupAdi).Alan("sipp", p => p.Sipp).Alan("donem", p => p.Donem)
        .Alan("hedefAdet", p => p.HedefAdet).Alan("gerceklesen", p => p.Gerceklesen).Alan("fark", p => p.Fark);

    private static async Task<Ok<Sayfa<FiloPlanDto>>> Liste(
        FleetPlanService svc, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
        => TypedResults.Ok(F5Ortak.Sayfala(
            (await svc.ListWithCountAsync(ct)).Select(s => FiloPlanDto.From(s)).ToList(), Harita, sayfa, boyut, sirala));

    private static async Task<FiloPlanDto?> DtoAsync(Guid id, FleetPlanService svc, CancellationToken ct)
    {
        var surum = await svc.VersionAsync(id, ct);
        var s = (await svc.ListWithCountAsync(ct)).FirstOrDefault(x => x.Hedef.Id == id);
        return s is null ? null : FiloPlanDto.From(s, surum);
    }

    private static async Task<Results<Ok<FiloPlanDto>, ProblemHttpResult>> Detay(Guid id, FleetPlanService svc, CancellationToken ct)
        => await DtoAsync(id, svc, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    private static FiloPlanInput Girdi(FiloPlanIstegi i)
    {
        AracFinansOrtak.Metin(i.AracGrupAdi, 64, "aracGrupAdi");
        AracFinansOrtak.Metin(i.Sipp, 16, "sipp");
        AracFinansOrtak.Metin(i.Donem, 32, "donem");
        AracFinansOrtak.Metin(i.Aciklama, 512, "aciklama");
        if (i.HedefAdet is < 0 or > 100_000)
            throw new ValidationException("Hedef adet 0 ile 100.000 arasında olmalıdır.", "hedefAdet");
        return new FiloPlanInput
        {
            AracGrupAdi = i.AracGrupAdi, Sipp = i.Sipp, Donem = i.Donem, HedefAdet = i.HedefAdet, Aciklama = i.Aciklama,
        };
    }

    private static async Task<Results<Created<FiloPlanDto>, ProblemHttpResult>> Olustur(
        FiloPlanIstegi i, FleetPlanService svc, CancellationToken ct)
    {
        var id = await svc.CreateAsync(Girdi(i), ct);
        return await DtoAsync(id, svc, ct) is { } d ? TypedResults.Created($"{Kok}/{id}", d) : Bulunamadi();
    }

    private static async Task<Results<Ok<FiloPlanDto>, ProblemHttpResult>> Guncelle(
        Guid id, FiloPlanIstegi i, FleetPlanService svc, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return Bulunamadi();
        var surum = AracFinansOrtak.Surum(i.Surum);
        if (!await svc.UpdateVersionedAsync(id, Girdi(i), surum, ct)) return Bulunamadi();
        return await DtoAsync(id, svc, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
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
        if (!await svc.ChangeTargetAsync(id, delta, ct)) return Bulunamadi();
        return await DtoAsync(id, svc, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Sil(Guid id, FleetPlanService svc, CancellationToken ct)
        => await svc.DeleteAsync(id, ct) ? TypedResults.NoContent() : Bulunamadi();
}
