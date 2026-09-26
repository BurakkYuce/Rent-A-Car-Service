using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DamageFiles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/hasar-dosyalari/*</c> (F6.1b) — hasar dosyası onay akışı (<see cref="DamageFileService"/>):
/// Açık → Onayda → Onaylandı/Reddedildi → Kapalı. MALİ BELGE DEĞİL; tahmini tutar bilgi.
/// <para><b>İzin:</b> OperationsWrite (Blazor grubu). <b>Kapsam:</b> dosya ARACIN şubesinden geçer (servis kapsam
/// uygulamıyor — kapı burası); tekil uçlarda durumdan ÖNCE 403. Geçişler satır kilidi altında (onayla + reddet yarışı
/// tek kazanır). Oluşturmada kira verilirse aynı araca ait ve kapsamda olmalı.</para>
/// </summary>
public static class DamageApi
{
    private const string Root = UiApiExtensions.V1 + "/hasar-dosyalari";

    public static RouteGroupBuilder MapDamageApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/hasar-dosyalari").WithTags("Hasar Dosyası").RequirePermission(Permission.OperationsWrite);
        g.MapGet("", GetList).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create);
        g.MapPost("/{id:guid}/onaya-gonder", (Guid id, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Transition(id, s, d, u, x => s.SendForApprovalAsync(id, ct), ct));
        g.MapPost("/{id:guid}/onayla", (Guid id, HasarNotIstegi? i, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Transition(id, s, d, u, x => s.ApproveAsync(id, Not(i), ct), ct));
        g.MapPost("/{id:guid}/reddet", (Guid id, HasarNotIstegi? i, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Transition(id, s, d, u, x => s.RejectAsync(id, Not(i), ct), ct));
        g.MapPost("/{id:guid}/kapat", (Guid id, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Transition(id, s, d, u, x => s.CloseAsync(id, ct), ct));
        return g;
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Hasar dosyası bulunamadı.");

    private static string? Not(HasarNotIstegi? i)
    {
        VehicleFinanceShared.Text(i?.Not, 512, "not");
        return VehicleFinanceShared.Nz(i?.Not);
    }

    private static readonly SortFieldMap<HasarDto> Map = SortFieldMap<HasarDto>
        .Create(f => f.Id)
        .Alan("no", f => f.No).Alan("plaka", f => f.Plaka).Alan("acilisTarihi", f => f.AcilisTarihi)
        .Alan("tahminiTutar", f => f.TahminiTutar).Alan("durum", f => f.Durum);

    private static async Task<Ok<Sayfa<HasarDto>>> GetList(
        DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, string? durum,
        Guid? vehicleId, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var d = F5Shared.EnumAdi<DamageStatus>(durum, "durum");
        var list = (await svc.ListAsync(ct)).Where(f => (d is null || f.Durum == d) && (vehicleId is null || f.VehicleId == vehicleId)).ToList();
        var fl = BranchScope.EffectiveFilter(kullanici);
        if (!fl.Unrestricted)
        {
            var branches = await VehicleFinanceShared.VehicleBranchesAsync(dbf, list.Select(f => f.VehicleId), ct);
            list = list.Where(f => VehicleFinanceShared.IsVisible(fl, f.VehicleId, branches)).ToList();
        }
        return TypedResults.Ok(F5Shared.Paginate(await DtosAsync(dbf, list, ct), Map, sayfa, boyut, sirala));
    }

    private static async Task<List<HasarDto>> DtosAsync(IDbContextFactory<AppDbContext> dbf, IReadOnlyList<DamageFile> l,
        CancellationToken ct)
    {
        var plates = await F5Shared.PlatesAsync(dbf, l.Select(f => f.VehicleId), ct);
        var customers = await F5Shared.CustomersAsync(dbf, l.Where(f => f.CariId is not null).Select(f => f.CariId!.Value), ct);
        return l.Select(f => HasarDto.From(f, F5Shared.Plate(plates, f.VehicleId),
            f.CariId is { } c ? F5Shared.CustomerName(customers, c) : null)).ToList();
    }

    private static async Task<DamageFile?> ComprehensiveAsync(Guid id, DamageFileService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        var f = await svc.GetAsync(id, ct);
        if (f is not null) await VehicleFinanceShared.RecordScopeAsync(dbf, user, f.VehicleId, ct); // durumdan ÖNCE
        return f;
    }

    private static async Task<Results<Ok<HasarDto>, ProblemHttpResult>> Detail(
        Guid id, DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
        => await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is { } f
            ? TypedResults.Ok((await DtosAsync(dbf, [f], ct))[0]) : NotFoundProblem();

    private static async Task<Results<Created<HasarDto>, ProblemHttpResult>> Create(
        HasarIstegi i, HttpContext http, DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
    {
        var key = IdempotencyHeader.Key(http);
        if (key is { } a && await svc.GetAsync(a, ct) is { } m) // (1) ÖNCE mevcut kayıt
            throw new DuplicateOperationException($"Bu hasar dosyası zaten kaydedildi (No {m.No}); yeni kayıt yazılmadı.",
                new MevcutIslem(m.Id, m.No, m.TahminiTutar ?? 0m, VehicleFinanceShared.BaseCurrency,
                    m.VehicleId == i.VehicleId && m.TahminiTutar == i.TahminiTutar));
        VehicleFinanceShared.InfoAmount(i.TahminiTutar, "tahminiTutar");
        VehicleFinanceShared.Text(i.Aciklama, 1024, "aciklama");
        await VehicleFinanceShared.VehicleWriteAsync(dbf, kullanici, i.VehicleId, "vehicleId", required: true, ct);
        await VehicleFinanceShared.CustomerExistsAsync(dbf, i.CariId, "cariId", required: false, ct);
        if (i.RentalId is { } r && r != Guid.Empty)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var rentalVehicle = await db.Rentals.AsNoTracking().Where(k => k.Id == r).Select(k => (Guid?)k.VehicleId).FirstOrDefaultAsync(ct);
            if (rentalVehicle is null) throw new ValidationException("Kira sözleşmesi bulunamadı.", "rentalId");
            if (rentalVehicle != i.VehicleId) throw new ValidationException("Kira sözleşmesi seçilen araca ait değil.", "rentalId");
        }
        var id = await svc.CreateAsync(new DamageFileInput
        {
            VehicleId = i.VehicleId, RentalId = i.RentalId is { } rr && rr != Guid.Empty ? rr : null,
            CariId = i.CariId is { } cc && cc != Guid.Empty ? cc : null, AcilisTarihi = F5Shared.Utc(i.AcilisTarihi),
            Aciklama = VehicleFinanceShared.Nz(i.Aciklama), TahminiTutar = i.TahminiTutar, IslemAnahtari = key,
        }, ct);
        var f = await svc.GetAsync(id, ct);
        return TypedResults.Created($"{Root}/{id}", (await DtosAsync(dbf, [f!], ct))[0]);
    }

    private static async Task<Results<Ok<HasarDto>, ProblemHttpResult>> Transition(
        Guid id, DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user,
        Func<Guid, Task<bool>> transition, CancellationToken ct)
    {
        if (await ComprehensiveAsync(id, svc, dbf, user, ct) is null) return NotFoundProblem(); // kapsam durumdan ÖNCE
        if (!await transition(id)) return NotFoundProblem();
        return await ComprehensiveAsync(id, svc, dbf, user, ct) is { } f
            ? TypedResults.Ok((await DtosAsync(dbf, [f], ct))[0]) : NotFoundProblem();
    }
}
