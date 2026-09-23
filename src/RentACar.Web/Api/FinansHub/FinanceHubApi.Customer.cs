using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Cari bakiye, bakiye düzeltme (Blazor <c>/finans/bakiye-duzeltme</c>), cari virman (<c>/cari-virman</c>).</summary>
public static partial class FinanceHubApi
{
    private static void MapCustomer(RouteGroupBuilder write, RouteGroupBuilder anyRead)
    {
        write.MapGet("/cariler/{cariId:guid}/bakiye", GetCustomerBalance);
        write.MapPost("/bakiye-duzeltme", PostBalanceAdjustment);
        write.MapGet("/cari-virmanlar", ListCustomerTransfers);
        write.MapPost("/cari-virman", PostCustomerTransfer);
        MapStatement(write, anyRead);
        MapBulk(write);
    }

    private static async Task<Results<Ok<CustomerBalance>, ProblemHttpResult>> GetCustomerBalance(
        Guid cariId, CashService cash, DepozitoService deposits, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var names = await F5Ortak.CarilerAsync(f, [cariId], ct);
        if (!names.ContainsKey(cariId)) return F5Ortak.Bulunamadi("Cari bulunamadı.");
        return TypedResults.Ok(new CustomerBalance(cariId, F5Ortak.CariAdi(names, cariId),
            await cash.GetCariBalanceAsync(cariId, ct), await deposits.GetBakiyeAsync(cariId, ct)));
    }

    /// <summary>E13: aynı içerik → 200 aynı id; başka cari/yön/tutar → 409 <c>mukerrer</c>. Kasa/Banka'ya dokunmaz;
    /// karşı bacak MuhasebeDuzeltmesi (P&amp;L raporlarına girmez).</summary>
    private static async Task<Ok<CashOperationResult>> PostBalanceAdjustment(
        BalanceAdjustmentRequest req, HttpContext http, BakiyeDuzeltmeService svc, IDbContextFactory<AppDbContext> f,
        CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var direction = F5Ortak.EnumAdi<BakiyeDuzeltmeYonu>(req.Yon, "yon")
                        ?? throw new ValidationException("Yön seçilmelidir (Alacaklandir ya da Borclandir).", "yon");
        var currency = MoneyInput(req.Tutar, req.Doviz, req.Kur);
        // Açıklama + " [makbuz]" defter açıklamasına (512) sığmalı.
        var receipt = Text(req.MakbuzNo, 32, "makbuzNo");
        var note = Text(req.Aciklama, 470, "aciklama");
        var date = MoneyDate(req.Tarih, "Bakiye düzeltme");
        var due = DueDate(req.Vade);
        await CustomerMustExistAsync(f, req.CariId, "cariId", ct);

        var id = await svc.AdjustAsync(new BakiyeDuzeltmeInput
        {
            CariId = req.CariId, Tutar = req.Tutar, Yon = direction, Doviz = currency, Kur = req.Kur,
            Tarih = date, Vade = due, MakbuzNo = receipt, Aciklama = note, IslemAnahtari = key,
        }, ct);
        return TypedResults.Ok(new CashOperationResult(id));
    }

    private static async Task<Ok<IReadOnlyList<CustomerTransferRow>>> ListCustomerTransfers(
        Guid? cariId, string? ara, DateOnly? bas, DateOnly? bit, int? limit,
        CashService cash, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        FinansApi.Metin(ara, 100, "ara");
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var rows = await cash.ListCariVirmanlarAsync(new CariVirmanFilter
        {
            CariId = cariId, Ara = F5Ortak.Nz(ara), Bas = min, Bit = max, EnFazla = Math.Clamp(limit ?? 200, 1, 1000),
        }, ct);
        // Görünen ad KVKK tek kuralıyla (MusteriGorunumu) — repo'nun ham adı kullanılmaz.
        var names = await F5Ortak.CarilerAsync(f, rows.SelectMany(r => new[] { r.KaynakCariId, r.HedefCariId }), ct);
        return TypedResults.Ok<IReadOnlyList<CustomerTransferRow>>(rows.Select(r => new CustomerTransferRow(
            r.Id, r.Tarih, r.Vade, r.KaynakCariId, F5Ortak.CariAdi(names, r.KaynakCariId),
            r.HedefCariId, F5Ortak.CariAdi(names, r.HedefCariId), r.Tutar, r.Doviz, r.Kur, r.TutarTl,
            r.MakbuzNo, r.Sube, r.IslemYapan, r.Aciklama)).ToList());
    }

    /// <summary>E07: aynı içerik → 200 aynı id (= işlem anahtarı); başka cari/tutar → 409 <c>mukerrer</c>.</summary>
    private static async Task<Ok<CashOperationResult>> PostCustomerTransfer(
        CustomerTransferRequest req, HttpContext http, CashService cash, IDbContextFactory<AppDbContext> f,
        CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var currency = MoneyInput(req.Tutar, req.Doviz, req.Kur);
        var receipt = Text(req.MakbuzNo, 32, "makbuzNo");
        var branch = Text(req.Sube, 128, "sube");
        var note = Text(req.Aciklama, 512, "aciklama");
        var date = MoneyDate(req.Tarih, "Virman");
        var due = DueDate(req.Vade);
        await CustomerMustExistAsync(f, req.KaynakCariId, "kaynakCariId", ct);
        await CustomerMustExistAsync(f, req.HedefCariId, "hedefCariId", ct);
        if (req.KaynakCariId == req.HedefCariId)
            throw new ValidationException("Kaynak ve hedef cari farklı olmalıdır.", "hedefCariId");

        await cash.TransferBetweenCariAsync(req.KaynakCariId, req.HedefCariId, req.Tutar, currency, req.Kur, note, key, ct,
            tarih: date, vade: due, makbuzNo: receipt, sube: branch);
        return TypedResults.Ok(new CashOperationResult(key));
    }

    /// <summary>Para tarihi: 2000'den önce değil, gelecekte değil (TarihPolitikasi; +1 gün tolerans) — alan'lı. UTC.</summary>
    internal static DateTimeOffset? MoneyDate(DateTimeOffset? value, string label, string field = "tarih")
    {
        if (value is { } t && t < TarihPolitikasi.EnErkenBelgeTarihi)
            throw new ValidationException($"{label} tarihi 2000 yılından önce olamaz.", field);
        FinansApi.Alanli(field, () => TarihPolitikasi.ParaTarihi(value, label));
        return Utc(value);
    }

    /// <summary>Vade (bilgi alanı): geleceğe açık ama makul pencerede [2000, bugün + 10 yıl]. UTC.</summary>
    internal static DateTimeOffset? DueDate(DateTimeOffset? value, string field = "vade")
    {
        if (value is { } v && (v < TarihPolitikasi.EnErkenBelgeTarihi || v > DateTimeOffset.UtcNow.AddYears(10)))
            throw new ValidationException("Vade tarihi 2000 ile bugünden 10 yıl sonrası arasında olmalıdır.", field);
        return Utc(value);
    }
}
