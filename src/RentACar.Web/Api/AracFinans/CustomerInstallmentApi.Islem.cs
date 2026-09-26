using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.AracFinans;

public static partial class CustomerInstallmentApi
{
    /// <summary>Plan: kimlikler anahtardan TÜRETİLİR (1. satır = anahtar); ikinci gönderim hiçbir satır yazmaz.</summary>
    private static async Task<Results<Created<TaksitPlanYaniti>, ProblemHttpResult>> Plan(
        TaksitPlanIstegi i, HttpContext http, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        if (i.TaksitSayisi is < 1 or > CustomerInstallmentService.MaxInstallments)
            throw new ValidationException($"Taksit sayısı 1 ile {CustomerInstallmentService.MaxInstallments} arasında olmalıdır.", "taksitSayisi");
        await PlanExistsAsync(key, i, svc, dbf, ct); // (1) ÖNCE mevcut plan

        VehicleFinanceShared.Amount(i.ToplamTutar, "toplamTutar", scale: 2);
        VehicleFinanceShared.Text(i.Aciklama, 512, "aciklama");
        var first = F5Shared.Utc(i.IlkVade);
        var (currency, exchangeRate) = await CurrencyRateAsync(i.Doviz, i.Kur, first, kurCozucu, ct);
        await ExistenceAsync(dbf, kullanici, i.CariId, i.VehicleId, i.VehicleSaleId, ct);
        int count;
        try
        {
            count = await svc.GeneratePlanAsync(new TaksitPlanInput
            {
                CariId = i.CariId, VehicleId = IfEmpty(i.VehicleId), VehicleSaleId = IfEmpty(i.VehicleSaleId),
                ToplamTutar = i.ToplamTutar, TaksitSayisi = i.TaksitSayisi, IlkVade = first, Currency = currency, Kur = exchangeRate,
                Aciklama = VehicleFinanceShared.Nz(i.Aciklama), IslemAnahtari = key,
            }, ct);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            await PlanExistsAsync(key, i, svc, dbf, ct);
            throw;
        }
        var ids = Enumerable.Range(1, count).Select(s => CustomerInstallmentService.PlanLineId(key, s)).ToList();
        return TypedResults.Created($"{Root}/{key}", new TaksitPlanYaniti(count, ids));
    }

    /// <summary>Aynı anahtarla yazılmış plan: <c>ayniIcerik</c> = aynı cari/araç/döviz, aynı adet ve aynı toplam.</summary>
    private static async Task PlanExistsAsync(Guid key, TaksitPlanIstegi i, CustomerInstallmentService svc,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(key, ct) is not { } first) return;
        var possible = Enumerable.Range(1, CustomerInstallmentService.MaxInstallments)
            .Select(s => CustomerInstallmentService.PlanLineId(key, s)).ToList();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var rows = await db.MusteriTaksitleri.AsNoTracking().Where(t => possible.Contains(t.Id))
            .Select(t => t.TaksitTutari).ToListAsync(ct);
        var total = rows.Sum();
        var currency = VehicleFinanceShared.Currency(i.Doviz); // yazımla AYNI normalizasyon (L1)
        var same = first.CariId == i.CariId && first.VehicleId == IfEmpty(i.VehicleId) && first.Currency == currency
                   && rows.Count == i.TaksitSayisi
                   && total == decimal.Round(i.ToplamTutar, 2, MidpointRounding.AwayFromZero);
        throw Existing(first.Id, first.Sira, total, first.Currency, same);
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Update(
        Guid id, MusteriTaksitIstegi i, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        if (await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is null) return NotFoundProblem(); // kapsam durumdan ÖNCE
        var version = VehicleFinanceShared.Version(i.Surum);
        var input = await InputAsync(i, dbf, kullanici, kurCozucu, ct); // yeni araç da kapsamda olmalı
        if (!await svc.UpdateVersionedAsync(id, input, version, ct)) return NotFoundProblem();
        return await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Paid(
        Guid id, TaksitOdendiIstegi? istek, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, CancellationToken ct)
    {
        if (await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is not { } checkedRow) return NotFoundProblem();
        var date = F5Shared.Utc(istek?.OdemeTarihi);
        WithFields("odemeTarihi", () => DatePolicy.MoneyDate(date, "Taksit ödeme"));
        if (!await svc.MarkPaidLockedAsync(id, true, date, ct, SameVehicleGuard(checkedRow.VehicleId))) return NotFoundProblem();
        return await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Undo(
        Guid id, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        if (await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is not { } checkedRow) return NotFoundProblem();
        if (!await svc.MarkPaidLockedAsync(id, false, null, ct, SameVehicleGuard(checkedRow.VehicleId))) return NotFoundProblem();
        return await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    /// <summary>
    /// Adversarial L4: kapsam kilidin DIŞINDA aracın şubesinden denetlendi; kilit altında taksidin aracı hâlâ o araç
    /// olmalı. Aksi halde (arada PUT ile başka araca taşındı) işlem yapılmaz → 409 <c>cakisma</c>, kayıt yeniden okunur.
    /// </summary>
    private static Action<MusteriTaksit> SameVehicleGuard(Guid? checkedVehicleId) => row =>
    {
        if (row.VehicleId != checkedVehicleId)
            throw new ConcurrentModificationException(ConcurrentModificationException.RecordMessage);
    };
}
