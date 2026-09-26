using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Kur;
using RentACar.Application.Regulation;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Common;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — PARA: sigorta / MTV / muayene ödemesi (dengeli defter: Borç Gider[araç] / Alacak Kasa-Banka). DEVIR §5 sırası:
/// kapsam (403) → (1) ÖNCE bu anahtarla / bu poliçe için yazılmış ödeme VAR mı (409 <c>mukerrer</c> + <c>mevcut</c> —
/// kaybolan yanıttan sonraki tekrar ikinci ödeme YAZMAZ) → (2) giriş kuralları (2 ondalık, 0 red, hesap-döviz çiti,
/// tutar × ÇÖZÜLEN kur baz sınırı) → (3) servis: satır kilidi altında anahtar + durum + bayatlık (<c>beklenenKalan</c>
/// → 409 <c>cakisma</c>), dönem kilidi, <c>KurCozucu</c>, dengeli küme.
/// </summary>
internal static partial class RegulationApi
{
    private const string PaymentAlreadySaved = "Bu ödeme zaten kaydedildi ({0}, {1} {2}); yeni ödeme yazılmadı.";
    private const string OtherPaymentSaved =
        "Bu işlem anahtarıyla başka içerikte bir ödeme kaydedilmiş ({0}, {1} {2}); girdiğiniz ödeme YAZILMADI. Kaydı kontrol edin.";

    private static void PaymentTexts(InstallmentPaymentRequest r)
    {
        S.Text(r.EvrakNo, 64, "evrakNo"); S.Text(r.IslemYapan, 128, "islemYapan"); S.Text(r.KasaKodu, 64, "kasaKodu");
        S.Text(r.HesapNo, 64, "hesapNo"); S.Text(r.Aciklama, 512, "aciklama");
        if (r.BeklenenKalan is { } bk) AracFinansOrtak.EnsureMaxScale(bk, 4, "beklenenKalan");
    }

    private static RegulasyonOdemeInput PaymentInput(InstallmentPaymentRequest r, Guid key) => new()
    {
        Tutar = r.Tutar, EvrakNo = r.EvrakNo, IslemYapan = r.IslemYapan, Aciklama = r.Aciklama, KasaKodu = r.KasaKodu,
        HesapNo = r.HesapNo, HesapId = r.HesapId is { } h && h != Guid.Empty ? h : null, IslemAnahtari = key,
        BeklenenKalan = r.BeklenenKalan,
    };

    /// <summary>Plain (field-less) service validation → <paramref name="field"/>.</summary>
    private static async Task Fielded(string field, Func<Task> check)
    {
        try { await check(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, field); }
    }

    private static async Task<Results<Ok<InstallmentPaymentResult>, ProblemHttpResult>> PayMtv(
        Guid id, InstallmentPaymentRequest r, HttpContext http, RegulationService reg, AccountResolver accounts,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        if (await ScopedMtvAsync(id, dbf, user, ct) is not { } m) return S.NotFound("MTV kaydı bulunamadı.");
        var account = S.CashAccount(r.Hesap);
        var date = S.Date(r.OdemeTarihi, "odemeTarihi");
        await ExistingInstallmentAsync(dbf, key, m.Id, $"MTV {m.Donem}", r, account, date, ct); // (1)
        S.PaymentAmount(r.Tutar, "tutar");
        if (r.Ceza is { } c && c != 0m) throw new ValidationException("MTV ödemesinde ceza girilmez.", "ceza");
        PaymentTexts(r);
        await Fielded("hesapId", () => accounts.ResolveAsync(r.HesapId, account, ct, "TRY"));
        RegulasyonOdemeSonuc result;
        try
        {
            result = await reg.PayMtvAsync(id, account, date, "TRY", null, PaymentInput(r, key), ct);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            await ExistingInstallmentAsync(dbf, key, m.Id, $"MTV {m.Donem}", r, account, date, ct); // race: report the winner
            throw;
        }
        return TypedResults.Ok(new InstallmentPaymentResult(result.OdemeId, result.Sira, result.Tutar, result.Kalan, result.Odendi));
    }

    private static async Task<Results<Ok<InstallmentPaymentResult>, ProblemHttpResult>> PayInspection(
        Guid id, InstallmentPaymentRequest r, HttpContext http, RegulationService reg, AccountResolver accounts,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        if (await ScopedInspectionAsync(id, dbf, user, ct) is not { } m) return S.NotFound("Muayene kaydı bulunamadı.");
        var account = S.CashAccount(r.Hesap);
        var date = S.Date(r.OdemeTarihi, "odemeTarihi");
        var label = $"Muayene {m.MuayeneTarihi:dd.MM.yyyy}";
        await ExistingInstallmentAsync(dbf, key, m.Id, label, r, account, date, ct); // (1)
        S.PaymentAmount(r.Tutar, "tutar");
        S.RecordAmount(r.Ceza, "ceza");
        PaymentTexts(r);
        await Fielded("hesapId", () => accounts.ResolveAsync(r.HesapId, account, ct, "TRY"));
        RegulasyonOdemeSonuc result;
        try
        {
            result = await reg.PayInspectionAsync(id, account, r.Ceza ?? 0m, date, "TRY", null, PaymentInput(r, key), ct);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            await ExistingInstallmentAsync(dbf, key, m.Id, label, r, account, date, ct);
            throw;
        }
        return TypedResults.Ok(new InstallmentPaymentResult(result.OdemeId, result.Sira, result.Tutar, result.Kalan, result.Odendi));
    }

    /// <summary>
    /// A payment already written with this key (MTV or muayene table) → 409 <c>mukerrer</c>. For THIS record: <c>mevcut</c>
    /// with <c>ayniIcerik</c> (amount — or "whole balance" when the request left it empty and that payment closed the
    /// record —, ceza, account type, specific account, explicit date). For another record/operation: no details leak.
    /// </summary>
    private static async Task ExistingInstallmentAsync(IDbContextFactory<AppDbContext> dbf, Guid key, Guid recordId, string label,
        InstallmentPaymentRequest r, LedgerAccountType account, DateTimeOffset? date, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mtv = await db.MtvOdemeleri.AsNoTracking().FirstOrDefaultAsync(x => x.IslemAnahtari == key, ct);
        var ins = mtv is null ? await db.MuayeneOdemeleri.AsNoTracking().FirstOrDefaultAsync(x => x.IslemAnahtari == key, ct) : null;
        if (mtv is null && ins is null) return;
        var (pid, owner, sira, amount, fine, after, acc, when, source) = mtv is not null
            ? (mtv.Id, mtv.MtvId, mtv.Sira, mtv.Tutar, 0m, mtv.KalanSonrasi, mtv.Hesap, mtv.Tarih, "MtvOdeme")
            : (ins!.Id, ins.InspectionId, ins.Sira, ins.Tutar, ins.Ceza, ins.KalanSonrasi, ins.Hesap, ins.Tarih, "MuayeneOdeme");
        if (owner != recordId) throw new DuplicateOperationException(AnahtarBaskaIslemde);
        var cashRef = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == source && e.SourceId == pid && e.Direction == LedgerDirection.Credit)
            .Select(e => e.AccountRef).FirstOrDefaultAsync(ct);
        var wantRef = r.HesapId is { } h && h != Guid.Empty ? h : (Guid?)null;
        var same = (r.Tutar is { } t ? amount == t : after == 0m) && fine == (r.Ceza ?? 0m) && acc == account
                   && cashRef == wantRef && AracFinansOrtak.AyniAn(when, date);
        var belge = $"{label} #{sira}";
        throw new DuplicateOperationException(string.Format(S.Tr, same ? PaymentAlreadySaved : OtherPaymentSaved, belge, S.Money(amount), "TRY"),
            new MevcutIslem(pid, belge, amount, "TRY", same));
    }

    // ------------------------------------------------------------------ sigorta (yapısal: poliçe başına tek ödeme)

    private static async Task<Results<Ok<InsurancePolicyDetail>, ProblemHttpResult>> PayPolicy(
        Guid id, InsurancePaymentRequest r, HttpContext http, RegulationService reg, AccountResolver accounts, ExchangeRateResolver rates,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedPolicyAsync(id, reg, dbf, user, ct) is not { } p) return S.NotFound("Sigorta poliçesi bulunamadı.");
        var account = S.CashAccount(r.Hesap);
        if (p.Odendi) throw await PolicyPaidAsync(dbf, p, r, account, ct); // (1) ÖNCE mevcut ödeme
        S.RecordAmount(r.ZeyilEkPrim, "zeyilEkPrim");
        var currency = AracFinansOrtak.Doviz(p.Currency, "kur");
        if (r.Kur is { } k) AracFinansOrtak.Kur(k, currency);
        var total = p.Prim + (r.ZeyilEkPrim ?? 0m);
        if (total <= 0m) throw new ValidationException("Sigorta ödeme tutarı pozitif olmalıdır.", "zeyilEkPrim");
        decimal rate = 1m;
        await Fielded("kur", async () => rate = await rates.ResolveAsync(currency, r.Kur, DateTimeOffset.UtcNow, ct));
        if (total * rate >= AracFinansOrtak.TutarUstSiniri) // F8.1a M1: base limit on the RESOLVED rate too
            throw new ValidationException("Ödeme tutarı × kur izin verilen büyüklüğü aşıyor.", currency == "TRY" ? "zeyilEkPrim" : "kur");
        await Fielded("hesapId", () => accounts.ResolveAsync(r.HesapId, account, ct, currency));
        try
        {
            await reg.PayInsuranceAsync(id, account, r.ZeyilEkPrim ?? 0m, null, r.Kur,
                r.HesapId is { } h && h != Guid.Empty ? h : null, ct);
        }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Message == "Sigorta zaten ödendi.")
        {
            if (await ScopedPolicyAsync(id, reg, dbf, user, ct) is { Odendi: true } won) throw await PolicyPaidAsync(dbf, won, r, account, ct);
            throw;
        }
        var d = await PolicyDetailAsync(id, http, reg, dbf, user, ct);
        return TypedResults.Ok(d!);
    }

    private static async Task<DuplicateOperationException> PolicyPaidAsync(IDbContextFactory<AppDbContext> dbf, InsurancePolicy p,
        InsurancePaymentRequest r, LedgerAccountType account, CancellationToken ct)
    {
        var trace = await LedgerTraceAsync(dbf, "SigortaOdeme", p.Id, ct);
        var wantRef = r.HesapId is { } h && h != Guid.Empty ? h : (Guid?)null;
        var same = trace is not null && p.ZeyilPrim == (r.ZeyilEkPrim ?? 0m) && trace.Hesap == account.ToString()
                   && trace.HesapId == wantRef;
        var total = p.Prim + p.ZeyilPrim;
        var belge = p.PoliceNo ?? p.Tip.ToString();
        return new DuplicateOperationException(
            string.Format(S.Tr, same ? PaymentAlreadySaved : OtherPaymentSaved, belge, S.Money(total), p.Currency),
            new MevcutIslem(p.Id, belge, total, p.Currency, same));
    }
}
