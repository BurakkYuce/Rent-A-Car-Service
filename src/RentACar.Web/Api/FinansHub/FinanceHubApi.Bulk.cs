using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Çok cari toplu tahsilat (Blazor <c>/toplu-tahsilat</c>, E03) ve toplu gider (<c>/toplu-gider</c>, E22).
/// İkisi de ATOMİK: bir satır geçersizse hiçbiri yazılmaz (servis doğrular, repo tek transaction'da yazar).</summary>
public static partial class FinanceHubApi
{
    public const string BulkAlreadyRecordedMessage = "Bu toplu tahsilat zaten kaydedildi ({0} satır, {1} TRY); yeni tahsilat yazılmadı.";
    public const string BulkOtherRecordedMessage =
        "Bu işlem anahtarıyla farklı içerikli bir toplu tahsilat yazılmış ({0} satır, {1} TRY); gönderdiğiniz liste YAZILMADI.";

    private const int BulkMaxLines = 500;

    private static void MapBulk(RouteGroupBuilder write)
    {
        write.MapPost("/toplu-tahsilat", PostBulkCollection)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        write.MapPost("/toplu-gider", PostBulkExpense);
    }

    /// <summary>E03: parti anahtarı = işlem anahtarı; satır anahtarı <c>RowKey(parti, i)</c>. ÖNCE bu partinin kaydı
    /// aranır (kaybolan yanıttan sonraki tekrar → 409 + <c>mevcut</c>; <c>ayniIcerik</c> tüm satırlar birebir aynıysa).</summary>
    private static async Task<Ok<BulkPostingResult>> PostBulkCollection(
        BulkCollectionRequest req, HttpContext http, CashService cash, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var account = FinansApi.Hesap(req.Hesap, "hesap");
        var channel = CashKanal.TryNormalize(req.Kanal)
                      ?? throw new ValidationException($"Geçersiz kanal. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.", "kanal");
        var lines = req.Satirlar ?? [];
        if (lines.Count == 0) throw new ValidationException("Toplu tahsilat en az bir satır içermelidir.", "satirlar");
        if (lines.Count > BulkMaxLines) throw new ValidationException($"Toplu tahsilat en çok {BulkMaxLines} satır olabilir.", "satirlar");
        var inputs = new List<CashInput>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.", $"satirlar[{i}].cariId");
            FinansApi.Tutar(l.Tutar, $"satirlar[{i}].tutar");
            AmountScale(l.Tutar, $"satirlar[{i}].tutar");
            inputs.Add(new CashInput
            {
                CariId = l.CariId, Tutar = l.Tutar, Hesap = account, HesapId = req.HesapId, Doviz = "TRY", Kur = 1m,
                Aciklama = Text(l.Aciklama, 512, $"satirlar[{i}].aciklama") ?? "Toplu tahsilat", Kanal = channel,
            });
        }
        var sum = inputs.Sum(x => x.Tutar);
        if (sum >= FinansApi.TutarUstSiniri) throw new ValidationException("Toplam tutar çok büyük.", "satirlar");
        // Var olmayan / başka kiracının carisi: servis toplu yolda cari varlığını denetlemez → uçta, satır alanıyla.
        var known = await F5Ortak.CarilerAsync(f, inputs.Select(x => x.CariId), ct);
        for (var i = 0; i < inputs.Count; i++)
            if (!known.ContainsKey(inputs[i].CariId))
                throw new ValidationException("Cari bulunamadı.", $"satirlar[{i}].cariId");

        await ThrowIfBatchRecordedAsync(key, inputs, cash, ct);
        await cash.BatchCollectAsync(inputs, key, ct);
        return TypedResults.Ok(new BulkPostingResult(inputs.Count, sum));
    }

    private static async Task ThrowIfBatchRecordedAsync(Guid key, List<CashInput> inputs, CashService cash, CancellationToken ct)
    {
        if (await cash.IslemAnahtariylaBulAsync(CashService.RowKey(key, 0), ct) is not { } first) return;
        var recorded = new List<CashTransaction> { first };
        for (var i = 1; ; i++)
        {
            if (i > BulkMaxLines || await cash.IslemAnahtariylaBulAsync(CashService.RowKey(key, i), ct) is not { } t) break;
            recorded.Add(t);
        }
        var same = recorded.Count == inputs.Count && recorded.Select((t, i) => (t, i)).All(x =>
        {
            var input = inputs[x.i];
            return x.t.Tip == CashTransactionType.Tahsilat && x.t.CariId == input.CariId && x.t.Amount.Amount == input.Tutar
                   && x.t.Amount.Currency == "TRY" && x.t.KarsiHesap == input.Hesap
                   && string.Equals(x.t.Kanal ?? CashKanal.Masaustu, input.Kanal, StringComparison.Ordinal)
                   && string.Equals(x.t.Aciklama, input.Aciklama, StringComparison.Ordinal);
        });
        var total = recorded.Sum(t => t.Amount.AmountInBase);
        var text = total.ToString("N2", Tr);
        throw new MukerrerIslemException(
            string.Format(Tr, same ? BulkAlreadyRecordedMessage : BulkOtherRecordedMessage, recorded.Count, text),
            new MevcutIslem(first.Id, first.No, total, "TRY", same));
    }

    /// <summary>E22: parti anahtarı = işlem anahtarı; tekrar 409 "Bu toplu gider zaten kaydedilmiş.". Araç kapsamı
    /// (403) ve varlığı (400) her satırda; tedarikçi cari varsa kiracıda olmalı.</summary>
    private static async Task<Ok<BulkPostingResult>> PostBulkExpense(
        BulkExpenseRequest req, HttpContext http, ExpenseService expenses, VehicleService vehicles,
        IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var type = F5Ortak.EnumAdi<ExpenseType>(req.Tip, "tip") ?? ExpenseType.Genel;
        var payment = F5Ortak.EnumAdi<OdemeYontemi>(req.OdemeYontemi, "odemeYontemi")
                      ?? throw new ValidationException("Ödeme yöntemi seçilmelidir.", "odemeYontemi");
        if (req.KdvOrani is < 0m or > 1m)
            throw new ValidationException("KDV oranı 0 ile 1 arasında bir kesir olmalıdır (0,20 = %20).", "kdvOrani");
        AmountScale(req.KdvOrani, "kdvOrani");
        var due = DueDate(req.Vade);
        if (req.CariId is { } cid) await CustomerMustExistAsync(f, cid, "cariId", ct);
        var lines = req.Satirlar ?? [];
        if (lines.Count == 0) throw new ValidationException("Toplu gider en az bir kalem içermelidir.", "satirlar");
        if (lines.Count > BulkMaxLines) throw new ValidationException($"Toplu gider en çok {BulkMaxLines} kalem olabilir.", "satirlar");

        var inputs = new List<ExpenseInput>(lines.Count);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            FinansApi.Tutar(l.NetTutar, $"satirlar[{i}].netTutar");
            AmountScale(l.NetTutar, $"satirlar[{i}].netTutar", maxDecimals: 2); // gider kuruşa yazılır
            FinansApi.BazSiniri(l.NetTutar, 1m + req.KdvOrani, $"satirlar[{i}].netTutar");
            if (l.AracId is { } vid && seen.Add(vid) && await vehicles.GetAsync(vid, ct) is null) // kapsam dışı → 403
                throw new ValidationException("Araç bulunamadı.", $"satirlar[{i}].aracId");
            inputs.Add(new ExpenseInput
            {
                Tip = type, NetTutar = l.NetTutar, KdvOrani = req.KdvOrani, Doviz = "TRY", Kur = 1m,
                OdemeYontemi = payment, KasaBankaHesap = payment == OdemeYontemi.Banka ? LedgerAccountType.Banka : LedgerAccountType.Kasa,
                VehicleId = l.AracId, CariId = req.CariId, Vade = due, FinansalHesapId = req.FinansalHesapId,
                Aciklama = Text(l.Aciklama, 512, $"satirlar[{i}].aciklama") ?? "Toplu gider",
            });
        }
        await expenses.BatchCreateAsync(inputs, key, ct);
        return TypedResults.Ok(new BulkPostingResult(inputs.Count, inputs.Sum(x => x.NetTutar)));
    }
}
