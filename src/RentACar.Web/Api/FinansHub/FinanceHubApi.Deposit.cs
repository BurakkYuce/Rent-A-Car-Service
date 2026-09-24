using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Finance;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Depozito ekranı (Blazor <c>/depozito</c>): tutulanlar listesi, iade (E10), mahsup (E11). Al (E09) ve irat
/// (E12) F4.4 uçlarıdır (<c>POST finans/depozito/al|irat</c>, <see cref="FinansApi"/>).</summary>
public static partial class FinanceHubApi
{
    private static void MapDeposit(RouteGroupBuilder write)
    {
        write.MapGet("/depozito", ListDeposits);
        write.MapPost("/depozito/iade", PostDepositRefund);
        write.MapPost("/depozito/mahsup", PostDepositOffset);
    }

    private static async Task<Ok<IReadOnlyList<DepositBalanceRow>>> ListDeposits(
        DepozitoService deposits, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var balances = await deposits.GetBakiyelerAsync(ct);
        var names = await F5Ortak.CarilerAsync(f, balances.Keys, ct);
        return TypedResults.Ok<IReadOnlyList<DepositBalanceRow>>(balances
            .Select(b => new DepositBalanceRow(b.Key, F5Ortak.CariAdi(names, b.Key), b.Value))
            .OrderBy(r => r.CariAd, StringComparer.Create(Tr, ignoreCase: true)).ToList());
    }

    /// <summary>E10: aynı içerik → 200 aynı id; başka cari/tutar/hesap → 409. Tutulanı aşan iade 400 (kilit altında).</summary>
    private static async Task<Ok<CashOperationResult>> PostDepositRefund(
        DepositRefundRequest req, HttpContext http, DepozitoService deposits, IDbContextFactory<AppDbContext> f,
        RentACar.Application.Kur.KurCozucu rates, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var account = FinansApi.Hesap(req.Hesap, "hesap");
        var currency = MoneyInput(req.Tutar, req.Doviz, req.Kur);
        await ResolvedBaseLimitAsync(rates, req.Tutar, currency, req.Kur, null, ct);
        await CustomerMustExistAsync(f, req.CariId, "cariId", ct);
        var id = await deposits.IadeAsync(req.CariId, req.Tutar, account, currency, req.Kur,
            tarih: null, islemAnahtari: key, hesapId: req.HesapId, ct: ct);
        return TypedResults.Ok(new CashOperationResult(id));
    }

    /// <summary>E11: aynı içerik → 200 aynı id; başka cari/tutar → 409. Tutulanı aşan mahsup 400.</summary>
    private static async Task<Ok<CashOperationResult>> PostDepositOffset(
        DepositOffsetRequest req, HttpContext http, DepozitoService deposits, IDbContextFactory<AppDbContext> f,
        RentACar.Application.Kur.KurCozucu rates, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var currency = MoneyInput(req.Tutar, req.Doviz, req.Kur);
        await ResolvedBaseLimitAsync(rates, req.Tutar, currency, req.Kur, null, ct);
        await CustomerMustExistAsync(f, req.CariId, "cariId", ct);
        var id = await deposits.MahsupAsync(req.CariId, req.Tutar, currency, req.Kur, tarih: null, islemAnahtari: key, ct: ct);
        return TypedResults.Ok(new CashOperationResult(id));
    }
}
