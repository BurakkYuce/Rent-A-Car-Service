using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class RegulationApi
{
    // ------------------------------------------------------------------ MTV

    private static readonly SiralamaHaritasi<MtvRow> MtvSort = SiralamaHaritasi<MtvRow>
        .Olustur(x => x.Id).Alan("plaka", x => x.Plaka).Alan("donem", x => x.Donem).Alan("vade", x => x.Vade)
        .Alan("tutar", x => x.Tutar).Alan("kalan", x => x.Kalan).Alan("odendi", x => x.Odendi);

    private static async Task<Ok<Sayfa<MtvRow>>> ListMtv(
        string? plaka, bool? odendi, DateOnly? vadeBas, DateOnly? vadeBit, int? sayfa, int? boyut, string? sirala,
        RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(vadeBas, vadeBit, "vadeBas", "vadeBit");
        var rows = (await reg.ListMtvAsync(ct)).Where(m => (odendi is null || m.Odendi == odendi)
            && (min is null || m.Vade >= min) && (max is null || m.Vade <= max));
        var visible = await S.VisibleAsync(dbf, user, rows, m => m.VehicleId, ct);
        var plates = await S.PlatesAsync(dbf, visible.Select(m => m.VehicleId), ct);
        var list = visible.Select(m => MtvRow.From(m, F5Ortak.Plaka(plates, m.VehicleId)))
            .Where(r => F5Ortak.Nz(plaka) is not { } q || r.Plaka.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, MtvSort, sayfa, boyut, sirala));
    }

    private static async Task<MtvRecord?> ScopedMtvAsync(Guid id, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var m = await db.MtvRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return null;
        await S.RecordScopeAsync(dbf, user, m.VehicleId, ct);
        return m;
    }

    private static async Task<Results<Ok<MtvDetail>, ProblemHttpResult>> MtvDetailEndpoint(
        Guid id, HttpContext http, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
        => await MtvDetailAsync(id, http, reg, dbf, user, ct) is { } d ? TypedResults.Ok(d) : S.NotFound("MTV kaydı bulunamadı.");

    private static async Task<MtvDetail?> MtvDetailAsync(Guid id, HttpContext http, RegulationService reg,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedMtvAsync(id, dbf, user, ct) is not { } m) return null;
        var payments = await reg.ListMtvOdemeAsync(id, ct);
        var accounts = await CashRefsAsync(dbf, "MtvOdeme", payments.Select(p => p.Id), ct);
        var rows = payments.Select(p => new InstallmentPaymentDto(p.Id, p.Sira, p.Tarih, p.Tutar, 0m, p.KalanSonrasi,
            p.Hesap.ToString(), accounts.GetValueOrDefault(p.Id), p.KasaKodu, p.HesapNo, p.EvrakNo, p.IslemYapan, p.Aciklama)).ToList();
        return new MtvDetail(MtvRow.From(m, await S.PlateAsync(dbf, m.VehicleId, ct)), rows, Actions(http, m.Odendi));
    }

    private static RegulationActions Actions(HttpContext http, bool paid)
        => new(!paid && AuthExtensions.HasPermission(http.User, Permission.FinanceWrite),
            AuthExtensions.HasPermission(http.User, Permission.OperationsWrite));

    /// <summary>Payment id → cash/bank account ref of its ledger credit leg (the real FAZ-50 link).</summary>
    private static async Task<Dictionary<Guid, Guid?>> CashRefsAsync(IDbContextFactory<AppDbContext> dbf, string sourceType,
        IEnumerable<Guid> paymentIds, CancellationToken ct)
    {
        var ids = paymentIds.ToList();
        if (ids.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == sourceType && ids.Contains(e.SourceId) && e.Direction == LedgerDirection.Credit)
            .Select(e => new { e.SourceId, e.AccountRef }).ToDictionaryAsync(e => e.SourceId, e => e.AccountRef, ct);
    }

    private static async Task<Results<Created<MtvDetail>, ProblemHttpResult>> CreateMtv(
        MtvRequest r, HttpContext http, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var key = IdempotencyBasligi.Anahtar(http);
        if (key is { } k && await ScopedMtvAsync(k, dbf, user, ct) is { } m) throw MtvDuplicate(m, r);
        S.RecordAmount(r.Tutar, "tutar");
        S.Text(r.Donem, 16, "donem"); S.Text(r.Aciklama, 512, "aciklama");
        var due = S.RequiredDate(r.Vade, "vade");
        await S.VehicleForWriteAsync(dbf, user, r.VehicleId, "vehicleId", ct);
        Guid id;
        try
        {
            id = await reg.AddMtvAsync(r.VehicleId!.Value, r.Donem ?? "", r.Tutar ?? 0m, due, r.Aciklama, ct, key);
        }
        catch (DbUpdateException ex) when (key is { } k2 && S.IsPrimaryKeyViolation(ex))
        {
            if (await ScopedMtvAsync(k2, dbf, user, ct) is { } won) throw MtvDuplicate(won, r);
            throw new MukerrerIslemException(AnahtarBaskaIslemde);
        }
        var d = await MtvDetailAsync(id, http, reg, dbf, user, ct);
        return TypedResults.Created($"{Root}/mtv/{id}", d!);
    }

    private static MukerrerIslemException MtvDuplicate(MtvRecord m, MtvRequest r)
        => new($"Bu MTV kaydı zaten eklendi ({m.Donem}); yeni kayıt yazılmadı.",
            new MevcutIslem(m.Id, m.Donem, m.Tutar, "TRY",
                m.VehicleId == r.VehicleId && m.Tutar == (r.Tutar ?? 0m) && m.Donem == (r.Donem ?? "").Trim()));

    // ------------------------------------------------------------------ muayene

    private static readonly SiralamaHaritasi<InspectionRow> InspectionSort = SiralamaHaritasi<InspectionRow>
        .Olustur(x => x.Id).Alan("plaka", x => x.Plaka).Alan("muayeneTarihi", x => x.MuayeneTarihi).Alan("bitis", x => x.Bitis)
        .Alan("ucret", x => x.Ucret).Alan("kalan", x => x.Kalan).Alan("odendi", x => x.Odendi);

    private static async Task<Ok<Sayfa<InspectionRow>>> ListInspections(
        string? plaka, bool? odendi, DateOnly? bitisBas, DateOnly? bitisBit, int? sayfa, int? boyut, string? sirala,
        RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(bitisBas, bitisBit, "bitisBas", "bitisBit");
        var rows = (await reg.ListInspectionAsync(ct)).Where(m => (odendi is null || m.Odendi == odendi)
            && (min is null || m.Bitis >= min) && (max is null || m.Bitis <= max));
        var visible = await S.VisibleAsync(dbf, user, rows, m => m.VehicleId, ct);
        var plates = await S.PlatesAsync(dbf, visible.Select(m => m.VehicleId), ct);
        var list = visible.Select(m => InspectionRow.From(m, F5Ortak.Plaka(plates, m.VehicleId)))
            .Where(r => F5Ortak.Nz(plaka) is not { } q || r.Plaka.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, InspectionSort, sayfa, boyut, sirala));
    }

    private static async Task<InspectionRecord?> ScopedInspectionAsync(Guid id, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var m = await db.InspectionRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return null;
        await S.RecordScopeAsync(dbf, user, m.VehicleId, ct);
        return m;
    }

    private static async Task<Results<Ok<InspectionDetail>, ProblemHttpResult>> InspectionDetailEndpoint(
        Guid id, HttpContext http, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
        => await InspectionDetailAsync(id, http, reg, dbf, user, ct) is { } d ? TypedResults.Ok(d) : S.NotFound("Muayene kaydı bulunamadı.");

    private static async Task<InspectionDetail?> InspectionDetailAsync(Guid id, HttpContext http, RegulationService reg,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedInspectionAsync(id, dbf, user, ct) is not { } m) return null;
        var payments = await reg.ListMuayeneOdemeAsync(id, ct);
        var accounts = await CashRefsAsync(dbf, "MuayeneOdeme", payments.Select(p => p.Id), ct);
        var rows = payments.Select(p => new InstallmentPaymentDto(p.Id, p.Sira, p.Tarih, p.Tutar, p.Ceza, p.KalanSonrasi,
            p.Hesap.ToString(), accounts.GetValueOrDefault(p.Id), p.KasaKodu, p.HesapNo, p.EvrakNo, p.IslemYapan, p.Aciklama)).ToList();
        return new InspectionDetail(InspectionRow.From(m, await S.PlateAsync(dbf, m.VehicleId, ct)), rows, Actions(http, m.Odendi));
    }

    private static async Task<Results<Created<InspectionDetail>, ProblemHttpResult>> CreateInspection(
        InspectionRequest r, HttpContext http, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user,
        CancellationToken ct)
    {
        var key = IdempotencyBasligi.Anahtar(http);
        if (key is { } k && await ScopedInspectionAsync(k, dbf, user, ct) is { } m) throw InspectionDuplicate(m, r);
        S.RecordAmount(r.Ucret, "ucret");
        S.IntRange(r.IslemKm, 0, 10_000_000, "islemKm");
        S.Text(r.Aciklama, 512, "aciklama");
        var date = S.RequiredDate(r.MuayeneTarihi, "muayeneTarihi");
        var end = S.RequiredDate(r.Bitis, "bitis");
        await S.VehicleForWriteAsync(dbf, user, r.VehicleId, "vehicleId", ct);
        Guid id;
        try
        {
            id = await reg.AddInspectionAsync(r.VehicleId!.Value, date, end, r.Ucret ?? 0m, r.IslemKm, r.Aciklama, ct, key);
        }
        catch (DbUpdateException ex) when (key is { } k2 && S.IsPrimaryKeyViolation(ex))
        {
            if (await ScopedInspectionAsync(k2, dbf, user, ct) is { } won) throw InspectionDuplicate(won, r);
            throw new MukerrerIslemException(AnahtarBaskaIslemde);
        }
        var d = await InspectionDetailAsync(id, http, reg, dbf, user, ct);
        return TypedResults.Created($"{Root}/muayeneler/{id}", d!);
    }

    private static MukerrerIslemException InspectionDuplicate(InspectionRecord m, InspectionRequest r)
        => new("Bu muayene kaydı zaten eklendi; yeni kayıt yazılmadı.",
            new MevcutIslem(m.Id, m.MuayeneTarihi.ToString("yyyy-MM-dd"), m.Ucret, "TRY",
                m.VehicleId == r.VehicleId && m.Ucret == (r.Ucret ?? 0m)));
}
