using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/musteri-taksitleri/*</c> (F6.1b) — müşteri taksit TAKİBİ (<see cref="CustomerInstallmentService"/>).
/// DEFTERE YAZMAZ: "ödendi" takip bayrağıdır, cari bakiyeyi kasa/banka tahsilatı değiştirir.
/// <para><b>İzin:</b> okuma FinanceWrite ∨ ViewReports, yazma FinanceWrite (Blazor grubu). <b>Kapsam:</b> taksit ARACIN
/// şubesinden geçer; araçsız taksit yalnız şube kısıtsız kullanıcıya görünür.</para>
/// <para><b>Çift gönderim:</b> oluşturma ve plan <c>Idempotency-Key</c> ister (kimlik = anahtar / anahtardan türetilmiş;
/// ikinci gönderim 409 <c>mukerrer</c> + <c>mevcut</c>). "Ödendi" kilit altında: ödenmiş taksidin yeniden işaretlenmesi
/// 409 <c>mukerrer</c> (tarih sessizce ezilmez). PUT tam değiştirmedir: zorunlu <c>surum</c>, bayat → 409 <c>cakisma</c>.</para>
/// </summary>
public static partial class CustomerInstallmentApi
{
    private const string Root = UiApiExtensions.V1 + "/musteri-taksitleri";

    public static RouteGroupBuilder MapCustomerInstallmentApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/musteri-taksitleri").WithTags("Müşteri Taksit");
        g.MapGet("", GetList).MapFields(F5Shared.SortRules)
            .RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/ozet", Summary).RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", Detail).RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", Create).RequirePermission(Permission.FinanceWrite)
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/plan", Plan).RequirePermission(Permission.FinanceWrite)
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPut("/{id:guid}", Update).RequirePermission(Permission.FinanceWrite);
        g.MapPost("/{id:guid}/odendi", Paid).RequirePermission(Permission.FinanceWrite)
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/{id:guid}/geri-al", Undo).RequirePermission(Permission.FinanceWrite);
        g.MapDelete("/{id:guid}", Delete).RequirePermission(Permission.FinanceWrite);
        return g;
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Taksit bulunamadı.");

    private static readonly SortFieldMap<MusteriTaksitSatiri> Map = SortFieldMap<MusteriTaksitSatiri>
        .Create(t => t.Id)
        .Alan("vade", t => t.Vade).Alan("sira", t => t.Sira).Alan("cari", t => t.CariAd).Alan("plaka", t => t.Plaka)
        .Alan("taksitTutari", t => t.TaksitTutari).Alan("tutarBaz", t => t.TutarBaz).Alan("durum", t => t.Durum);

    private static async Task<List<MusteriTaksit>> SetAsync(CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, Guid? customerId, Guid? vehicleId, string? status, DateOnly? dueMin, DateOnly? dueMax,
        bool? overdue, CancellationToken ct)
    {
        var (min, max) = F5Shared.DayRange(dueMin, dueMax);
        var list = await svc.SearchAsync(new MusteriTaksitFilter
        {
            CariId = customerId, VehicleId = vehicleId, Durum = F5Shared.EnumAdi<InstallmentStatus>(status, "durum"),
            VadeMin = min, VadeMax = max, SadeceGecikmis = overdue,
        }, ct);
        var f = BranchScope.EffectiveFilter(user);
        if (f.Unrestricted) return list.ToList();
        var branches = await VehicleFinanceShared.VehicleBranchesAsync(dbf,
            list.Where(t => t.VehicleId is not null).Select(t => t.VehicleId!.Value), ct);
        return list.Where(t => VehicleFinanceShared.IsVisible(f, t.VehicleId, branches)).ToList();
    }

    private static async Task<Ok<Sayfa<MusteriTaksitSatiri>>> GetList(
        CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId,
        Guid? vehicleId, string? durum, DateOnly? vadeMin, DateOnly? vadeMax, bool? gecikmis, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var list = await SetAsync(svc, dbf, kullanici, cariId, vehicleId, durum, vadeMin, vadeMax, gecikmis, ct);
        var customers = await F5Shared.CustomersAsync(dbf, list.Select(t => t.CariId), ct);
        var plates = await F5Shared.PlatesAsync(dbf, list.Where(t => t.VehicleId is not null).Select(t => t.VehicleId!.Value), ct);
        var rows = list.Select(t => MusteriTaksitSatiri.From(t, F5Shared.CustomerName(customers, t.CariId),
            t.VehicleId is { } v ? F5Shared.Plate(plates, v) : null)).ToList();
        return TypedResults.Ok(F5Shared.Paginate(rows, Map, sayfa, boyut, sirala));
    }

    /// <summary>Özet (tutarlar BAZ para — karışık dövizde toplanabilsin). Filtreli + kapsamlı küme.</summary>
    private static async Task<Ok<TaksitOzet>> Summary(
        CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId,
        Guid? vehicleId, string? durum, DateOnly? vadeMin, DateOnly? vadeMax, bool? gecikmis, CancellationToken ct)
        => TypedResults.Ok(CustomerInstallmentService.Summary(
            await SetAsync(svc, dbf, kullanici, cariId, vehicleId, durum, vadeMin, vadeMax, gecikmis, ct)));

    private static async Task<MusteriTaksit?> ComprehensiveAsync(Guid id, CustomerInstallmentService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var t = await svc.GetAsync(id, ct);
        if (t is null) return null;
        await VehicleFinanceShared.RecordScopeAsync(dbf, user, t.VehicleId, ct); // durumdan ÖNCE (403)
        return t;
    }

    private static async Task<MusteriTaksitSatiri?> DtoAsync(Guid id, CustomerInstallmentService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var version = await svc.VersionAsync(id, ct); // alanlardan ÖNCE
        var t = await ComprehensiveAsync(id, svc, dbf, user, ct);
        if (t is null) return null;
        var account = F5Shared.CustomerName(await F5Shared.CustomersAsync(dbf, [t.CariId], ct), t.CariId);
        var plate = t.VehicleId is { } v ? F5Shared.Plate(await F5Shared.PlatesAsync(dbf, [v], ct), v) : null;
        return MusteriTaksitSatiri.From(t, account, plate, version);
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Detail(
        Guid id, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
        => await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        Guid id, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        if (await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is null) return NotFoundProblem();
        return await svc.DeleteAsync(id, ct) ? TypedResults.NoContent() : NotFoundProblem();
    }
}
