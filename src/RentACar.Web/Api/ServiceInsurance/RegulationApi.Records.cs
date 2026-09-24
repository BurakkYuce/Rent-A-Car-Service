using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class RegulationApi
{
    // ------------------------------------------------------------------ sigorta

    private static readonly SiralamaHaritasi<InsurancePolicyRow> PolicySort = SiralamaHaritasi<InsurancePolicyRow>
        .Olustur(x => x.Id).Alan("plaka", x => x.Plaka).Alan("tip", x => x.Tip).Alan("bitis", x => x.Bitis)
        .Alan("prim", x => x.Prim).Alan("kalan", x => x.Kalan).Alan("odendi", x => x.Odendi);

    private static async Task<Ok<Sayfa<InsurancePolicyRow>>> ListPolicies(
        string? plaka, bool? odendi, string? tip, DateOnly? bitisBas, DateOnly? bitisBit, int? sayfa, int? boyut, string? sirala,
        RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var t = F5Ortak.EnumAdi<InsuranceType>(tip, "tip");
        var (min, max) = F5Ortak.GunAraligi(bitisBas, bitisBit, "bitisBas", "bitisBit");
        var rows = (await reg.ListInsuranceAsync(ct)).Where(p => (odendi is null || p.Odendi == odendi) && (t is null || p.Tip == t)
            && (min is null || p.Bitis >= min) && (max is null || p.Bitis <= max));
        var visible = await S.VisibleAsync(dbf, user, rows, p => p.VehicleId, ct);
        var plates = await S.PlatesAsync(dbf, visible.Select(p => p.VehicleId), ct);
        var list = visible.Select(p => InsurancePolicyRow.From(p, F5Ortak.Plaka(plates, p.VehicleId)))
            .Where(r => F5Ortak.Nz(plaka) is not { } q || r.Plaka.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, PolicySort, sayfa, boyut, sirala));
    }

    /// <summary>Policy passed through the vehicle-branch gate (403 BEFORE any state), or null (404).</summary>
    private static async Task<InsurancePolicy?> ScopedPolicyAsync(Guid id, RegulationService reg,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var p = await db.InsurancePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        await S.RecordScopeAsync(dbf, user, p.VehicleId, ct);
        return p;
    }

    private static async Task<Results<Ok<InsurancePolicyDetail>, ProblemHttpResult>> PolicyDetail(
        Guid id, HttpContext http, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
        => await PolicyDetailAsync(id, http, reg, dbf, user, ct) is { } d ? TypedResults.Ok(d) : S.NotFound("Sigorta poliçesi bulunamadı.");

    private static async Task<InsurancePolicyDetail?> PolicyDetailAsync(Guid id, HttpContext http, RegulationService reg,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedPolicyAsync(id, reg, dbf, user, ct) is not { } p) return null;
        var zeyiller = (await reg.ListZeyilAsync(id, ct)).Select(EndorsementDto.From).ToList();
        var trace = await LedgerTraceAsync(dbf, "SigortaOdeme", id, ct);
        var actions = new RegulationActions(!p.Odendi && AuthExtensions.HasPermission(http.User, Permission.FinanceWrite),
            AuthExtensions.HasPermission(http.User, Permission.OperationsWrite));
        return new InsurancePolicyDetail(InsurancePolicyRow.From(p, await S.PlateAsync(dbf, p.VehicleId, ct)), zeyiller, trace, actions);
    }

    /// <summary>Payment trace from the LEDGER (credit leg = cash/bank account).</summary>
    private static async Task<LedgerPaymentTrace?> LedgerTraceAsync(IDbContextFactory<AppDbContext> dbf, string sourceType,
        Guid sourceId, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var credit = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == sourceType && e.SourceId == sourceId && e.Direction == LedgerDirection.Credit)
            .FirstOrDefaultAsync(ct);
        return credit is null ? null : new LedgerPaymentTrace(credit.EntryDateUtc, credit.Amount.Amount, credit.Amount.Currency,
            credit.Amount.Rate, credit.Amount.AmountInBase, credit.AccountType.ToString(), credit.AccountRef);
    }

    private static async Task<Results<Created<InsurancePolicyDetail>, ProblemHttpResult>> CreatePolicy(
        InsurancePolicyRequest r, HttpContext http, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user,
        CancellationToken ct)
    {
        var key = IdempotencyBasligi.Anahtar(http);
        if (key is { } k && await ScopedPolicyAsync(k, reg, dbf, user, ct) is { } m) // (1) ÖNCE mevcut kayıt
            throw PolicyDuplicate(m, r);
        S.RecordAmount(r.Prim, "prim");
        S.RecordAmount(r.AracDegeri, "aracDegeri"); S.RecordAmount(r.ImmDegeri, "immDegeri"); S.RecordAmount(r.AksesuarDegeri, "aksesuarDegeri");
        S.Text(r.PoliceNo, 64, "policeNo"); S.Text(r.Firma, 128, "firma"); S.Text(r.Acenta, 128, "acenta");
        var tip = F5Ortak.EnumAdi<InsuranceType>(r.Tip, "tip") ?? throw new ValidationException("Sigorta tipi seçilmelidir.", "tip");
        var start = S.RequiredDate(r.Baslangic, "baslangic");
        var end = S.RequiredDate(r.Bitis, "bitis");
        await S.VehicleForWriteAsync(dbf, user, r.VehicleId, "vehicleId", ct);
        Guid id;
        try
        {
            id = await reg.AddInsuranceAsync(r.VehicleId!.Value, tip, start, end, r.Prim ?? 0m, r.PoliceNo, r.Firma, r.Acenta,
                r.Doviz, r.AracDegeri, r.ImmDegeri, r.AksesuarDegeri, ct, key);
        }
        catch (DbUpdateException ex) when (key is { } k2 && S.IsPrimaryKeyViolation(ex))
        {
            if (await ScopedPolicyAsync(k2, reg, dbf, user, ct) is { } won) throw PolicyDuplicate(won, r);
            throw new MukerrerIslemException(AnahtarBaskaIslemde);
        }
        var d = await PolicyDetailAsync(id, http, reg, dbf, user, ct);
        return TypedResults.Created($"{Root}/sigortalar/{id}", d!);
    }

    public const string AnahtarBaskaIslemde =
        "Bu işlem anahtarı başka bir işlemde kullanılmış; kayıt yazılmadı. Kayıtları kontrol edip yeni işlem başlatın.";

    private static MukerrerIslemException PolicyDuplicate(InsurancePolicy m, InsurancePolicyRequest r)
    {
        var same = m.VehicleId == r.VehicleId && string.Equals(m.Tip.ToString(), r.Tip?.Trim(), StringComparison.OrdinalIgnoreCase)
                   && m.Prim == (r.Prim ?? 0m);
        return new MukerrerIslemException($"Bu poliçe zaten kaydedildi ({m.PoliceNo ?? m.Tip.ToString()}); yeni kayıt yazılmadı.",
            new MevcutIslem(m.Id, m.PoliceNo ?? "", m.Prim, m.Currency, same));
    }

    private static async Task<Results<Created<EndorsementDto>, ProblemHttpResult>> AddEndorsement(
        Guid id, EndorsementRequest r, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedPolicyAsync(id, reg, dbf, user, ct) is null) return S.NotFound("Sigorta poliçesi bulunamadı.");
        S.RecordAmount(r.Deger, "deger");
        S.RecordAmount(r.Brut, "brut", allowNegative: true); S.RecordAmount(r.Net, "net", allowNegative: true);
        S.RecordAmount(r.FonVergi, "fonVergi", allowNegative: true);
        S.Text(r.ZeyilNo, 32, "zeyilNo"); S.Text(r.Tipi, 64, "tipi"); S.Text(r.Neden, 512, "neden");
        var zid = await reg.AddZeyilAsync(new ZeyilInput
        {
            PolicyId = id, ZeyilNo = r.ZeyilNo, Tarih = S.Date(r.Tarih, "tarih"), Tanzim = S.Date(r.Tanzim, "tanzim"),
            Deger = r.Deger, Brut = r.Brut, Net = r.Net, FonVergi = r.FonVergi, Tipi = r.Tipi, Neden = r.Neden,
        }, ct);
        var z = (await reg.ListZeyilAsync(id, ct)).First(x => x.Id == zid);
        return TypedResults.Created($"{Root}/sigortalar/{id}", EndorsementDto.From(z));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEndorsement(
        Guid id, RegulationService reg, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            var policyVehicle = await db.InsurancePolicyZeyilleri.AsNoTracking().Where(z => z.Id == id)
                .Join(db.InsurancePolicies.AsNoTracking(), z => z.PolicyId, p => p.Id, (z, p) => (Guid?)p.VehicleId)
                .FirstOrDefaultAsync(ct);
            if (policyVehicle is null) return S.NotFound("Zeyil kaydı bulunamadı.");
            await S.RecordScopeAsync(dbf, user, policyVehicle, ct);
        }
        await reg.DeleteZeyilAsync(id, ct);
        return TypedResults.NoContent();
    }
}
