using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.AracFinans;

public static partial class VehicleLoanApi
{
    public const string InstallmentAlreadyPaid = "Bu taksit ödemesi zaten kaydedildi (Gider No {0}, {1} {2}); yeni ödeme yazılmadı.";
    public const string InstallmentOtherPayment =
        "Bu işlem anahtarıyla başka içerikte bir taksit ödemesi kaydedilmiş (Gider No {0}, {1} {2}); girdiğiniz ödeme " +
        "YAZILMADI. Güncel taksit planını kontrol edin.";
    public const string KeyInOtherOperation =
        "Bu işlem anahtarı başka bir işlemde kullanılmış; taksit ödenmedi. Kayıtları kontrol edip yeni işlem başlatın.";

    private static async Task<Results<Ok<TaksitOdeYaniti>, ProblemHttpResult>> PayInstallment(
        Guid id, TaksitOdeIstegi istek, HttpContext http, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurResolver, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        var k = await ComprehensiveAsync(id, svc, dbf, kullanici, ct); // kapsam durumdan ÖNCE (403)
        if (k is null) return NotFoundProblem();
        var account = AccountType(istek.Hesap);
        var date = F5Shared.Utc(istek.OdemeTarihi);

        // (1) ÖNCE bu anahtarla yazılmış ödeme (kaybolan yanıttan sonraki tekrar ikinci taksidi ÖDEMEZ).
        await ExistingInstallmentAsync(dbf, key, k, istek, account, date, ct);

        // (2) Giriş kuralları; bayatlık (sira ≠ ödenen+1) kilit altında serviste → 409 cakisma.
        if (istek.Sira < 1 || istek.Sira > k.TaksitSayisi)
            throw new ValidationException($"Taksit sırası 1 ile {k.TaksitSayisi} arasında olmalıdır.", "sira");
        await WithFields("odemeTarihi", () => { DatePolicy.MoneyDate(date, "Taksit"); return Task.CompletedTask; });
        // Adversarial L3: dövizli kredide ödeme günü kuru bulunamazsa hata errors[kur] ile döner (servis de aynı
        // çözümü yapar; burada yalnız alanlı ön kontrol — TRY'de kur daima 1, çağrı gereksiz).
        if (k.Currency != VehicleFinanceShared.BaseCurrency)
            await WithFields("kur", () => kurResolver.ResolveAsync(k.Currency, null, date ?? DateTimeOffset.UtcNow, ct));

        bool paid;
        try
        {
            paid = await svc.PayInstallmentAsync(id, account, date, key, istek.HesapId, ct, expectedSequence: istek.Sira);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            // Yarış: aynı anahtarla eşzamanlı ikinci istek kilit içi anahtar çitine takıldı → yazılmış kaydı bildir.
            await ExistingInstallmentAsync(dbf, key, k, istek, account, date, ct);
            throw;
        }
        if (!paid) throw new ConcurrentModificationException("Kredinin tüm taksitleri ödenmiş; taksit ödenmedi.");

        await using var db = await dbf.CreateDbContextAsync(ct);
        var g = await db.Expenses.AsNoTracking().FirstAsync(e => e.IslemAnahtari == key, ct);
        var detail = await DetailAsync(id, http, svc, dbf, kullanici, ct);
        return TypedResults.Ok(new TaksitOdeYaniti(g.Id, g.No, istek.Sira, g.GenelToplam, g.Currency, detail!));
    }

    /// <summary>
    /// Aynı anahtarla yazılmış gider varsa 409 <c>mukerrer</c>. Bu kredinin taksidiyse <c>mevcut</c> döner;
    /// <c>ayniIcerik</c>: aynı taksit sırası, hesap türü, spesifik hesap ve (açık verildiyse) tarih. Anahtar başka bir
    /// işlemin (başka kredi, başka gider) ise ayrıntı SIZDIRILMAZ.
    /// </summary>
    private static async Task ExistingInstallmentAsync(IDbContextFactory<AppDbContext> dbf, Guid key, AracKredi k,
        TaksitOdeIstegi request, LedgerAccountType account, DateTimeOffset? date, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var g = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.IslemAnahtari == key, ct);
        if (g is null) return;
        var prefix = $"Kredi taksiti {k.No} #";
        if (g.Tip != ExpenseType.Finansman || g.Aciklama is not { } a || !a.StartsWith(prefix, StringComparison.Ordinal))
            throw new DuplicateOperationException(KeyInOtherOperation);
        var orderText = a[prefix.Length..].Split('/')[0];
        var order = int.TryParse(orderText, out var s) ? s : 0;
        var accountId = request.HesapId is { } h && h != Guid.Empty ? h : (Guid?)null;
        var same = order == request.Sira && g.KasaBankaHesap == account && g.FinansalHesapId == accountId
                   && VehicleFinanceShared.SameInstant(g.Tarih, date);
        var amount = g.GenelToplam.ToString("N2", Tr);
        throw new DuplicateOperationException(
            string.Format(Tr, same ? InstallmentAlreadyPaid : InstallmentOtherPayment, g.No, amount, g.Currency),
            new MevcutIslem(g.Id, g.No, g.GenelToplam, g.Currency, same));
    }
}
