using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/arac-kredileri/*</c> (F6.1b) — araç kredisi. İş mantığı <see cref="VehicleLoanService"/>'te.
/// <para><b>İzin:</b> okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (servis <c>SearchAsync</c> ile aynı); oluşturma
/// OperationsWrite; taksit ödeme FinanceWrite (defter yazar — Blazor adversarial 1.3 M2); iptal OperationsDelete.</para>
/// <para><b>Kapsam:</b> kredi ARACIN şubesinden geçer (alt kayıt kuralı). Araçsız kredi yalnız şube kısıtsız kullanıcıya
/// görünür. Tekil uçlarda kapsam durumdan ÖNCE (403).</para>
/// <para><b>Para (DEVIR §5):</b> taksit ödeme = gerçek gider + dengeli defter (Borç Gider[araç] / Alacak Kasa-Banka),
/// <c>Idempotency-Key</c> zorunlu. Sıra: (1) bu anahtarla yazılmış gider VAR mı → 409 <c>mukerrer</c> + <c>mevcut</c>
/// (kaybolan yanıttan sonraki tekrar ikinci taksidi ödemez); (2) SONRA bayatlık: istemcinin ödemek istediği
/// <c>sira</c> kilit altında <c>OdenenTaksit + 1</c> ile karşılaştırılır → 409 <c>cakisma</c>. Kur servisteki
/// <see cref="ExchangeRateResolver"/>'den (TRY = 1). Oluşturma da anahtarlı: kredi Id'si = anahtar (ikinci kredi PK'ye çarpar).</para>
/// </summary>
public static partial class VehicleLoanApi
{
    private const string Root = UiApiExtensions.V1 + "/arac-kredileri";

    /// <summary>Kredi tutarı üst sınırı: faiz (≤ %1000) × 30 yıl ile toplam geri ödeme <c>numeric(19,4)</c>'e sığar.</summary>
    public const decimal MaxLoanAmount = 1_000_000_000_000m;
    public const decimal MaxInterestRate = 10m;

    public static RouteGroupBuilder MapVehicleLoanApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/arac-kredileri").WithTags("Araç Kredisi");
        g.MapGet("", GetList).MapFields(F5Shared.SortRules)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/ozet", Summary)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", Detail)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", Create).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/taksit-ode", PayInstallment).RequirePermission(Permission.FinanceWrite)
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        g.MapPost("/toplu-iptal", BulkCancel).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Kredi bulunamadı.");

    private static readonly SortFieldMap<AracKrediListeSatiri> Map = SortFieldMap<AracKrediListeSatiri>
        .Create(k => k.Id)
        .Alan("no", k => k.No).Alan("bankaAdi", k => k.BankaAdi).Alan("plaka", k => k.Plaka)
        .Alan("cari", k => k.CariAd).Alan("krediTutari", k => k.KrediTutari).Alan("baslangicTarihi", k => k.BaslangicTarihi)
        .Alan("kalanBakiye", k => k.KalanBakiye).Alan("durum", k => k.Durum);

    /// <summary>Filtreli + kapsamlı kredi kümesi (liste ve özet kartları AYNI kümeden).</summary>
    private static async Task<List<AracKredi>> SetAsync(VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, Guid? customerId, string? plate, string? fileNo, string? status, DateOnly? start, DateOnly? bit,
        CancellationToken ct)
    {
        var (min, max) = F5Shared.DayRange(start, bit);
        var list = await svc.SearchAsync(new AracKrediFilter
        {
            CariId = customerId, Plaka = F5Shared.Nz(plate), DosyaNo = F5Shared.Nz(fileNo),
            Durum = F5Shared.EnumAdi<LoanStatus>(status, "durum"), Bas = min, Bit = max,
        }, ct);
        var f = BranchScope.EffectiveFilter(user);
        if (f.Unrestricted) return list.ToList();
        var branches = await VehicleFinanceShared.VehicleBranchesAsync(dbf, list.Where(k => k.VehicleId is not null)
            .Select(k => k.VehicleId!.Value), ct);
        return list.Where(k => VehicleFinanceShared.IsVisible(f, k.VehicleId, branches)).ToList();
    }

    private static async Task<Ok<Sayfa<AracKrediListeSatiri>>> GetList(
        VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId, string? plaka,
        string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut, string? sirala,
        CancellationToken ct)
    {
        var list = await SetAsync(svc, dbf, kullanici, cariId, plaka, dosyaNo, durum, bas, bit, ct);
        var customers = await F5Shared.CustomersAsync(dbf, list.Where(k => k.CariId is not null).Select(k => k.CariId!.Value), ct);
        var plates = await F5Shared.PlatesAsync(dbf, list.Where(k => k.VehicleId is not null).Select(k => k.VehicleId!.Value), ct);
        var rows = list.Select(k =>
        {
            var oz = VehicleLoanService.Calculate(k);
            return new AracKrediListeSatiri(k.Id, k.No, k.BankaAdi, k.VehicleId,
                k.VehicleId is { } v ? F5Shared.Plate(plates, v) : null, k.CariId,
                k.CariId is { } c ? F5Shared.CustomerName(customers, c) : null, k.DosyaNo, k.KrediTutari, k.FaizOran,
                k.TaksitSayisi, k.OdenenTaksit, k.BaslangicTarihi, k.Currency, k.Durum.ToString(),
                oz.ToplamGeriOdeme, oz.AylikTaksit, oz.KalanBakiye, oz.SonVadeGunu);
        }).ToList();
        return TypedResults.Ok(F5Shared.Paginate(rows, Map, sayfa, boyut, sirala));
    }

    /// <summary>Liste üstü 5 özet kart (filtreli küme; iptal hariç). Salt gösterge — deftere yazmaz.</summary>
    private static async Task<Ok<AracKrediPano>> Summary(
        VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId, string? plaka,
        string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit, CancellationToken ct)
        => TypedResults.Ok(VehicleLoanService.Dashboard(
            await SetAsync(svc, dbf, kullanici, cariId, plaka, dosyaNo, durum, bas, bit, ct), DateTimeOffset.UtcNow));

    private static async Task<Results<Ok<AracKrediDetayYaniti>, ProblemHttpResult>> Detail(
        Guid id, HttpContext http, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
        => await DetailAsync(id, http, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();

    /// <summary>Kredi (kapsam kapısından geçmiş) ya da null.</summary>
    private static async Task<AracKredi?> ComprehensiveAsync(Guid id, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        var k = await svc.GetAsync(id, ct);
        if (k is null) return null;
        await VehicleFinanceShared.RecordScopeAsync(dbf, user, k.VehicleId, ct); // durumdan ÖNCE (403)
        return k;
    }

    private static async Task<AracKrediDetayYaniti?> DetailAsync(Guid id, HttpContext http, VehicleLoanService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var k = await ComprehensiveAsync(id, svc, dbf, user, ct);
        if (k is null) return null;
        var plate = k.VehicleId is { } v ? F5Shared.Plate(await F5Shared.PlatesAsync(dbf, [v], ct), v) : null;
        var account = k.CariId is { } c ? F5Shared.CustomerName(await F5Shared.CustomersAsync(dbf, [c], ct), c) : null;
        var active = k.Durum == LoanStatus.Aktif;
        var y = new AracKrediYetkileri(
            active && AuthExtensions.HasPermission(http.User, Permission.FinanceWrite),
            active && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete));
        return AracKrediDetayYaniti.From(k, plate, account, y);
    }
}
