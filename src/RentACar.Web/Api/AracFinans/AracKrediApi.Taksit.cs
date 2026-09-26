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

public static partial class AracKrediApi
{
    public const string TaksitZatenOdendi = "Bu taksit ödemesi zaten kaydedildi (Gider No {0}, {1} {2}); yeni ödeme yazılmadı.";
    public const string TaksitBaskaOdeme =
        "Bu işlem anahtarıyla başka içerikte bir taksit ödemesi kaydedilmiş (Gider No {0}, {1} {2}); girdiğiniz ödeme " +
        "YAZILMADI. Güncel taksit planını kontrol edin.";
    public const string AnahtarBaskaIslemde =
        "Bu işlem anahtarı başka bir işlemde kullanılmış; taksit ödenmedi. Kayıtları kontrol edip yeni işlem başlatın.";

    private static async Task<Results<Ok<TaksitOdeYaniti>, ProblemHttpResult>> TaksitOde(
        Guid id, TaksitOdeIstegi istek, HttpContext http, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurResolver, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        var k = await KapsamliAsync(id, svc, dbf, kullanici, ct); // kapsam durumdan ÖNCE (403)
        if (k is null) return Bulunamadi();
        var hesap = HesapTuru(istek.Hesap);
        var tarih = F5Ortak.Utc(istek.OdemeTarihi);

        // (1) ÖNCE bu anahtarla yazılmış ödeme (kaybolan yanıttan sonraki tekrar ikinci taksidi ÖDEMEZ).
        await MevcutTaksitAsync(dbf, anahtar, k, istek, hesap, tarih, ct);

        // (2) Giriş kuralları; bayatlık (sira ≠ ödenen+1) kilit altında serviste → 409 cakisma.
        if (istek.Sira < 1 || istek.Sira > k.TaksitSayisi)
            throw new ValidationException($"Taksit sırası 1 ile {k.TaksitSayisi} arasında olmalıdır.", "sira");
        await Alanli("odemeTarihi", () => { DatePolicy.MoneyDate(tarih, "Taksit"); return Task.CompletedTask; });
        // Adversarial L3: dövizli kredide ödeme günü kuru bulunamazsa hata errors[kur] ile döner (servis de aynı
        // çözümü yapar; burada yalnız alanlı ön kontrol — TRY'de kur daima 1, çağrı gereksiz).
        if (k.Currency != AracFinansOrtak.TemelDoviz)
            await Alanli("kur", () => kurResolver.ResolveAsync(k.Currency, null, tarih ?? DateTimeOffset.UtcNow, ct));

        bool odendi;
        try
        {
            odendi = await svc.PayInstallmentAsync(id, hesap, tarih, anahtar, istek.HesapId, ct, expectedSequence: istek.Sira);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            // Yarış: aynı anahtarla eşzamanlı ikinci istek kilit içi anahtar çitine takıldı → yazılmış kaydı bildir.
            await MevcutTaksitAsync(dbf, anahtar, k, istek, hesap, tarih, ct);
            throw;
        }
        if (!odendi) throw new ConcurrentModificationException("Kredinin tüm taksitleri ödenmiş; taksit ödenmedi.");

        await using var db = await dbf.CreateDbContextAsync(ct);
        var g = await db.Expenses.AsNoTracking().FirstAsync(e => e.IslemAnahtari == anahtar, ct);
        var detay = await DetayAsync(id, http, svc, dbf, kullanici, ct);
        return TypedResults.Ok(new TaksitOdeYaniti(g.Id, g.No, istek.Sira, g.GenelToplam, g.Currency, detay!));
    }

    /// <summary>
    /// Aynı anahtarla yazılmış gider varsa 409 <c>mukerrer</c>. Bu kredinin taksidiyse <c>mevcut</c> döner;
    /// <c>ayniIcerik</c>: aynı taksit sırası, hesap türü, spesifik hesap ve (açık verildiyse) tarih. Anahtar başka bir
    /// işlemin (başka kredi, başka gider) ise ayrıntı SIZDIRILMAZ.
    /// </summary>
    private static async Task MevcutTaksitAsync(IDbContextFactory<AppDbContext> dbf, Guid anahtar, AracKredi k,
        TaksitOdeIstegi istek, LedgerAccountType hesap, DateTimeOffset? tarih, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var g = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.IslemAnahtari == anahtar, ct);
        if (g is null) return;
        var onek = $"Kredi taksiti {k.No} #";
        if (g.Tip != ExpenseType.Finansman || g.Aciklama is not { } a || !a.StartsWith(onek, StringComparison.Ordinal))
            throw new DuplicateOperationException(AnahtarBaskaIslemde);
        var siraMetni = a[onek.Length..].Split('/')[0];
        var sira = int.TryParse(siraMetni, out var s) ? s : 0;
        var hesapId = istek.HesapId is { } h && h != Guid.Empty ? h : (Guid?)null;
        var ayni = sira == istek.Sira && g.KasaBankaHesap == hesap && g.FinansalHesapId == hesapId
                   && AracFinansOrtak.AyniAn(g.Tarih, tarih);
        var tutar = g.GenelToplam.ToString("N2", Tr);
        throw new DuplicateOperationException(
            string.Format(Tr, ayni ? TaksitZatenOdendi : TaksitBaskaOdeme, g.No, tutar, g.Currency),
            new MevcutIslem(g.Id, g.No, g.GenelToplam, g.Currency, ayni));
    }
}
