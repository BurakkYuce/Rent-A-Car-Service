using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Tek cari toplu kapatma (Blazor <c>/finans/tek-cari-kapat</c>, envanter E05).</summary>
public static partial class FinanceHubApi
{
    /// <summary>Kaybolan yanıttan sonraki tekrar: aynı anahtarla bu cariye yazılmış kapatma tahsilatı.</summary>
    public const string CloseAlreadyRecordedMessage = "Bu toplu kapatma zaten kaydedildi (No {0}, {1} TRY); yeni tahsilat yazılmadı.";
    public const string CloseOtherRecordedMessage =
        "Bu işlem anahtarıyla başka bir kapatma yazıldı (No {0}, {1} TRY); gönderdiğiniz seçim YAZILMADI. Güncel kalemleri kontrol edin.";

    /// <summary>
    /// E05: seçilen borç kalemlerini TEK tahsilatla kapatır (baz para, TRY); tahsis kalıcıdır. Sıra: giriş → cari var
    /// mı (404) → kira şube kapsamı (403, durumdan önce) → ÖNCE bu anahtarla kayıt var mı (409 <c>mukerrer</c> +
    /// <c>mevcut</c>) → servis (tahsis/bakiye çitleri cari danışma kilidi altında, aynı transaction).
    /// </summary>
    private static async Task<Results<Ok<CloseItemsResult>, ProblemHttpResult>> PostCloseItems(
        Guid cariId, CloseItemsRequest req, HttpContext http, CashService cash, RentalService rentals,
        ICurrentUser user, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var account = FinansApi.Hesap(req.Hesap, "hesap");
        var channel = CashKanal.TryNormalize(req.Kanal)
                      ?? throw new ValidationException($"Geçersiz kanal. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.", "kanal");
        var note = Text(req.Aciklama, 512, "aciklama");
        var date = MoneyDate(req.Tarih, "İşlem");
        var selection = req.Secim ?? [];
        if (selection.Count == 0) throw new ValidationException("En az bir kalem seçilmelidir.", "secim");
        if (selection.Count > 500) throw new ValidationException("Tek seferde en çok 500 kalem kapatılabilir.", "secim");
        var map = new Dictionary<Guid, decimal?>(selection.Count);
        for (var i = 0; i < selection.Count; i++)
        {
            var s = selection[i];
            if (s.KalemId == Guid.Empty || !map.TryAdd(s.KalemId, s.Tutar))
                throw new ValidationException("Kalem boş ya da iki kez seçilmiş.", $"secim[{i}].kalemId");
            if (s.Tutar is { } v)
            {
                // Servis kalemi kuruşa (2 ondalık) yazar; fazla hane SESSİZCE kesilmesin diye 400.
                FinansApi.Tutar(v, $"secim[{i}].tutar");
                AmountScale(v, $"secim[{i}].tutar", maxDecimals: 2);
            }
        }

        var names = await F5Ortak.CarilerAsync(f, [cariId], ct);
        if (!names.ContainsKey(cariId)) return F5Ortak.Bulunamadi("Cari bulunamadı.");
        await SelectionRentalScopeAsync(map.Keys, user, rentals, f, ct);

        if (await cash.IslemAnahtariylaBulAsync(key, ct) is { } prior)
        {
            if (prior.CariId != cariId || prior.Tip != CashTransactionType.Tahsilat)
                throw MukerrerIslemException.FarkliIcerik();
            // L3: aynı içerik = aynı kalem kümesi + (tutar verilen kalemde) aynı tahsis tutarı + hesap/kanal/açıklama.
            // Tahsis kaydı kalıcıdır; tutarsız ("kalanın tamamı") kalemde tutar ilk yazımda belirlendiği için
            // yalnız kalemin varlığı karşılaştırılır.
            await using var db = await f.CreateDbContextAsync(ct);
            var allocations = await db.KapatmaTahsisleri.AsNoTracking()
                .Where(t => t.CashTransactionId == prior.Id)
                .Select(t => new { t.LedgerEntryId, t.KapatilanBaz }).ToListAsync(ct);
            var sameItems = allocations.Count == map.Count && allocations.All(a =>
                map.TryGetValue(a.LedgerEntryId, out var requested)
                && (requested is not { } v || decimal.Round(v, 2, MidpointRounding.ToZero) == a.KapatilanBaz));
            var same = sameItems && prior.KarsiHesap == account
                       && string.Equals(prior.Kanal ?? CashKanal.Masaustu, channel, StringComparison.Ordinal)
                       && (note is null || string.Equals(FinansApi.AciklamaNorm(prior.Aciklama), note, StringComparison.Ordinal));
            var amount = prior.Amount.Amount.ToString("N2", Tr);
            throw new MukerrerIslemException(
                string.Format(Tr, same ? CloseAlreadyRecordedMessage : CloseOtherRecordedMessage, prior.No, amount),
                new MevcutIslem(prior.Id, prior.No, prior.Amount.Amount, prior.Amount.Currency, same));
        }

        var total = await cash.TekCariTopluKapatAsync(cariId, map, account, date, note, key, channel, ct);
        var written = await cash.IslemAnahtariylaBulAsync(key, ct);
        return TypedResults.Ok(new CloseItemsResult(written?.Id ?? Guid.Empty, written?.No ?? "", total));
    }

    /// <summary>
    /// Kapsamlı (şubeye bağlı) kullanıcı: seçilen kalemlerin fatura → kira zinciri kapsamda olmalı. Servis tahsilatı
    /// tek kiraya bağlayıp o kiranın Tahsilat/Bakiye alanını düşürür; başka şubenin kirası buradan değişmesin.
    /// </summary>
    private static async Task SelectionRentalScopeAsync(
        IEnumerable<Guid> ledgerIds, ICurrentUser user, RentalService rentals, IDbContextFactory<AppDbContext> f,
        CancellationToken ct)
    {
        if (BranchScope.EffectiveFilter(user).Unrestricted) return;
        var ids = ledgerIds.ToList();
        await using var db = await f.CreateDbContextAsync(ct);
        var invoiceIds = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.SourceType == "Fatura").Select(e => e.SourceId).Distinct().ToListAsync(ct);
        if (invoiceIds.Count == 0) return;
        var rentalIds = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .Select(i => i.RentalId ?? i.KaynakKiraId).Where(r => r != null).Select(r => r!.Value).Distinct().ToListAsync(ct);
        foreach (var rentalId in rentalIds) await FinansApi.KiraKapsamdaAsync(rentals, rentalId, ct);
    }
}
