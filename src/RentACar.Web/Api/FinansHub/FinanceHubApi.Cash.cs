using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Kasa hub (Blazor <c>/kasa</c>): özet, nakit işlem listesi, virman geçmişi, virman, ters kayıt.</summary>
public static partial class FinanceHubApi
{
    /// <summary>Liste tavanı (Blazor KasaHub ile aynı: önce sunucuda süz, SONRA kes).</summary>
    internal const int CashListCap = 2000;

    private static void MapCash(RouteGroupBuilder write)
    {
        write.MapGet("/kasa/ozet", GetCashboxSummary);
        write.MapGet("/kasa/islemler", ListCashTransactions);
        write.MapGet("/kasa/virmanlar", ListCashTransfers);
        write.MapPost("/kasa/virman", PostCashTransfer);
        // Ters kayıt defteri geri sarar: dar izin (Blazor /finans/tahsilat/ters ile aynı).
        write.MapPost("/kasa/islemler/{id:guid}/ters", ReverseCashTransaction)
            .RequirePermission(Permission.FinanceReverse);
    }

    private static async Task<Ok<CashboxSummary>> GetCashboxSummary(ReportService reports, CancellationToken ct)
    {
        var s = await reports.GetCashBankSummaryAsync(ct: ct);
        return TypedResults.Ok(new CashboxSummary(s.KasaGiris, s.KasaCikis, s.KasaBakiye, s.BankaGiris, s.BankaCikis, s.BankaBakiye));
    }

    private static async Task<Ok<CashTransactionList>> ListCashTransactions(
        string? q, string? tip, string? hesap, Guid? hesapId, string? kanal, DateOnly? bas, DateOnly? bit,
        int? sayfa, int? boyut, CashService cash, FinancialAccountService accounts, IDbContextFactory<AppDbContext> f,
        CancellationToken ct)
    {
        var (page, size) = Paging(sayfa, boyut);
        var type = F5Ortak.EnumAdi<CashTransactionType>(tip, "tip");
        LedgerAccountType? account = string.IsNullOrWhiteSpace(hesap) ? null : FinansApi.Hesap(hesap, "hesap");
        string? channel = null;
        if (!string.IsNullOrWhiteSpace(kanal))
            channel = CashKanal.TryNormalize(kanal)
                      ?? throw new ValidationException($"Geçersiz kanal. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.", "kanal");
        FinansApi.Metin(q, 100, "q");
        var (min, max) = F5Ortak.GunAraligi(bas, bit);

        var rows = await cash.SearchTransactionsAsync(new CashFilter
        {
            Ara = F5Ortak.Nz(q), Tip = type, Hesap = account, HesapId = hesapId, Bas = min, Bit = max,
            EnFazla = CashListCap,
        }, ct);
        var capped = rows.Count >= CashListCap;
        // Kanal süzgeci Blazor semantiği: NULL (FAZ-84 öncesi kayıt) "Masaüstü" sayılır.
        var filtered = channel is null ? rows : rows.Where(x => (x.Islem.Kanal ?? CashKanal.Masaustu) == channel).ToList();
        var pageRows = filtered.Skip((page - 1) * size).Take(size).ToList();

        var names = await F5Ortak.CarilerAsync(f, pageRows.Select(r => r.Islem.CariId), ct);
        var accountNames = await AccountNamesAsync(accounts, ct);
        var items = pageRows.Select(r =>
        {
            var t = r.Islem;
            return new CashTransactionRow(
                t.Id, t.No, t.Tarih, t.Tip.ToString(), t.TersKayitMi, t.TersAlinanId,
                t.KarsiHesap.ToString(), t.HesapId, t.HesapId is { } h && accountNames.TryGetValue(h, out var ad) ? ad : null,
                t.Kanal ?? CashKanal.Masaustu, t.CariId, F5Ortak.CariAdi(names, t.CariId), r.CariKod, t.RentalId,
                t.Amount.Amount, t.Amount.Currency, t.Amount.Rate, t.Amount.AmountInBase, t.Aciklama);
        }).ToList();
        return TypedResults.Ok(new CashTransactionList(new Sayfa<CashTransactionRow>(items, filtered.Count, page, size), capped));
    }

    private static async Task<Ok<IReadOnlyList<CashTransferRow>>> ListCashTransfers(
        DateOnly? bas, DateOnly? bit, Guid? hesapId, string? ara, int? limit,
        CashService cash, FinancialAccountService accounts, CancellationToken ct)
    {
        FinansApi.Metin(ara, 100, "ara");
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var rows = await cash.ListCashTransfersAsync(new KasaVirmanFilter
        {
            Bas = min, Bit = max, HesapId = hesapId, Ara = F5Ortak.Nz(ara), EnFazla = Math.Clamp(limit ?? 50, 1, 500),
        }, ct);
        var accountNames = await AccountNamesAsync(accounts, ct);
        string? Name(Guid? id) => id is { } x && accountNames.TryGetValue(x, out var n) ? n : null;
        return TypedResults.Ok<IReadOnlyList<CashTransferRow>>(rows.Select(v => new CashTransferRow(
            v.Id, v.Tarih, v.KaynakTur.ToString(), v.KaynakHesapId, Name(v.KaynakHesapId),
            v.HedefTur.ToString(), v.HedefHesapId, Name(v.HedefHesapId),
            v.Tutar, v.Doviz, v.Kur, v.TutarTl, v.MakbuzNo, v.Sube, v.IslemYapan, v.Aciklama, v.KunyeVar)).ToList());
    }

    /// <summary>E06: aynı içerik → 200 aynı id (= işlem anahtarı); farklı tutar/yön/hesap → 409 <c>mukerrer</c>.</summary>
    private static async Task<Ok<CashOperationResult>> PostCashTransfer(
        CashTransferRequest req, HttpContext http, CashService cash, RentACar.Application.Kur.ExchangeRateResolver rates,
        CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var source = FinansApi.Hesap(req.Kaynak, "kaynak");
        var target = FinansApi.Hesap(req.Hedef, "hedef");
        var currency = MoneyInput(req.Tutar, req.Doviz, req.Kur);
        await ResolvedBaseLimitAsync(rates, req.Tutar, currency, req.Kur, null, ct); // servis bugünkü kuru çözer
        var receipt = Text(req.MakbuzNo, 32, "makbuzNo");
        var branch = Text(req.Sube, 128, "sube");
        var note = Text(req.Aciklama, 512, "aciklama");
        await cash.TransferAsync(source, target, req.Tutar, currency, req.Kur, note, key,
            sourceAccountId: req.KaynakHesapId, targetAccountId: req.HedefHesapId, receiptNo: receipt, branch: branch, ct: ct);
        return TypedResults.Ok(new CashOperationResult(key));
    }

    /// <summary>E08 (yapısal): ikinci ters kayıt 409 <c>mukerrer</c>; ters kaydın tersi 400. Kira bağlı işlemde kira
    /// şube kapsamı DURUMDAN ÖNCE (başka şube 403); olmayan/başka kiracının işlemi 404.</summary>
    private static async Task<Results<Ok<CashOperationResult>, ProblemHttpResult>> ReverseCashTransaction(
        Guid id, CashService cash, RentalService rentals, RentACar.Domain.Common.ICurrentUser user, CancellationToken ct)
    {
        if (await cash.GetAsync(id, ct) is not { } tx) return F5Ortak.Bulunamadi("Kasa işlemi bulunamadı.");
        if (tx.RentalId is { } rentalId) await FinansApi.KiraKapsamdaAsync(rentals, rentalId, ct);
        // L4b: kirasız kasa işleminin şubesi yok (kasa/banka hesabı şubeye bağlı değil). Şubeye bağlı kullanıcı
        // yalnız kendi şubesinin kirasına bağlı işlemi ters alabilir; kiracı geneli kasa işlemi kapsam dışıdır.
        else if (!BranchScope.EffectiveFilter(user).Unrestricted)
            throw new NoPermissionException("Kiraya bağlı olmayan kasa işlemini yalnız şube kısıtı olmayan kullanıcı ters alabilir.");
        return TypedResults.Ok(new CashOperationResult(await cash.ReverseAsync(id, ct)));
    }

    internal static (int Page, int Size) Paging(int? sayfa, int? boyut)
    {
        if (sayfa is < 1 or > 100_000) throw new ValidationException("Sayfa 1 ile 100000 arasında olmalıdır.", "sayfa");
        if (boyut is < 1 or > 200) throw new ValidationException("Sayfa boyutu 1 ile 200 arasında olmalıdır.", "boyut");
        return (sayfa ?? 1, boyut ?? 50);
    }

    private static async Task<Dictionary<Guid, string>> AccountNamesAsync(FinancialAccountService accounts, CancellationToken ct)
        => (await accounts.ListAsync(ct)).ToDictionary(h => h.Id, h => string.IsNullOrWhiteSpace(h.Ad) ? h.Kod : h.Ad);
}
