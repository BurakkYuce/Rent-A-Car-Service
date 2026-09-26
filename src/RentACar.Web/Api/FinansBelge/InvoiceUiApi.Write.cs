using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

public static partial class InvoiceUiApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string ManualAlreadySaved = "Bu fatura zaten kesildi (No {0}, {1} {2}); yeni fatura kesilmedi.";
    public const string ManualOtherSaved =
        "Bu işlem anahtarıyla başka bir fatura kesilmiş (No {0}, {1} {2}); girdiğiniz fatura KESİLMEDİ. Kayıtları kontrol edin.";

    /// <summary>
    /// Manuel fatura (E14). Sıra (DEVIR §5): giriş sınırları → kapsam (kiracı geneli: şubeli kullanıcı 403) → cari
    /// varlığı → ANAHTARLA YAZILMIŞ FATURA VAR MI (409 + mevcut) → servis (dönem kilidi, KDV satır bazlı yuvarlama,
    /// boşluksuz no aynı transaction'da, dengeli defter). Yarışı kaybeden istek serviste PK'ye çarpar ve aynı
    /// içerikte mevcut faturayı döner (ikinci belge yazılmaz).
    /// </summary>
    private static async Task<Ok<DocumentResult>> CreateManual(
        ManualInvoiceRequest req, HttpContext http, InvoiceService invoices, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        if (req.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.", "cariId");
        Amount(req.NetTutar, "netTutar");
        var rate = req.KdvOrani ?? 0.20m;
        VatRate(rate, "kdvOrani");
        BaseLimit(req.NetTutar * (1m + rate), 1m, "netTutar"); // M2: brüt (net + KDV) numeric(19,4)'e sığmalı
        Text(req.Aciklama, 512, "aciklama");
        Text(req.IslemSube, 128, "islemSube");
        Text(req.EvrakNo, 64, "evrakNo");
        Text(req.FaturaOzelKod, 64, "faturaOzelKod");
        Text(req.OdemeTuru, 32, "odemeTuru");
        Text(req.GonderimSekli, 32, "gonderimSekli");
        Text(req.KdvSifirSebep, 128, "kdvSifirSebep");
        AmountLimit(req.Otv, "otv");
        AmountLimit(req.TevkifatTutar, "tevkifatTutar");
        AmountLimit(req.DamgaVergisi, "damgaVergisi");
        WithField("tarih", () => DatePolicy.MoneyDate(req.Tarih, "Fatura"));
        RequireUnrestricted(user);

        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            await RequireCustomerAsync(db, req.CariId, "cariId", ct);
            if (await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == key, ct) is { } existing)
            {
                var (vat, _) = VatMath.FromNet(req.NetTutar, rate);
                var same = existing.ManuelMi && !existing.IadeMi && existing.RentalId is null && existing.KaynakKiraId is null
                           && existing.CariId == req.CariId && existing.NetTutar == VatMath.RoundGross(req.NetTutar)
                           && existing.KdvTutar == vat;
                var amount = existing.GenelToplam.ToString("N2", Tr);
                throw new DuplicateOperationException(
                    string.Format(Tr, same ? ManualAlreadySaved : ManualOtherSaved, existing.No, amount, existing.Currency),
                    new MevcutIslem(existing.Id, existing.No, existing.GenelToplam, existing.Currency, same));
            }
        }

        var id = await invoices.CreateManualAsync(new ManualInvoiceInput
        {
            CariId = req.CariId, NetTutar = req.NetTutar, KdvOrani = rate, Aciklama = Trimmed(req.Aciklama),
            Tarih = F5Shared.Utc(req.Tarih), VadeTarihi = F5Shared.Utc(req.VadeTarihi), IslemAnahtari = key,
            IslemSube = Trimmed(req.IslemSube), EvrakNo = Trimmed(req.EvrakNo), FaturaOzelKod = Trimmed(req.FaturaOzelKod),
            OdemeTuru = Trimmed(req.OdemeTuru), GonderimSekli = Trimmed(req.GonderimSekli),
            KdvSifirSebep = Trimmed(req.KdvSifirSebep),
            Vergi = new InvoiceTaxInfo(req.Otv, req.TevkifatOran, req.TevkifatTutar, req.DamgaVergisi),
        }, ct);
        return TypedResults.Ok(await ResultAsync(invoices, id, ct));
    }

    /// <summary>İade faturası (E17, yapısal): kapsam → servis (tek iade ön-kontrol + kısmi unique, dönem kilidi,
    /// ters defter kümesi). Başlık kullanılmaz; ikinci iade 400 "zaten iade edilmiş".</summary>
    private static async Task<Results<Ok<DocumentResult>, ProblemHttpResult>> CreateRefund(
        Guid id, InvoiceService invoices, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var inv = await invoices.GetAsync(id, ct);
        if (inv is null) return F5Shared.NotFound("Fatura bulunamadı.");
        await using (var db = await dbf.CreateDbContextAsync(ct))
            RequireInScope(user, (await InvoiceBranchesAsync(db, [inv], ct))[inv.Id]);
        var refundId = await invoices.CreateRefundAsync(id, ct: ct);
        return TypedResults.Ok(await ResultAsync(invoices, refundId, ct));
    }

    /// <summary>Toplu kira faturası (E16). Kapsam kapısı hepsine TEK TEK ve kesimden ÖNCE (kısmen kesilip 403
    /// dönen parti olmasın). Her kira servisteki tekil yoldan geçer; ikinci gönderim yeni belge üretmez.</summary>
    private static async Task<Ok<BatchInvoiceResult>> CreateBatch(
        BatchInvoiceRequest req, InvoiceService invoices, RentalService rentals, CancellationToken ct)
    {
        var ids = (req.KiraIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("En az bir kira seçilmelidir.", "kiraIds");
        if (ids.Count > InvoiceService.MaxBulkSelection)
            throw new ValidationException($"Tek seferde en çok {InvoiceService.MaxBulkSelection} kira faturalanabilir.", "kiraIds");
        VatRate(req.KdvOrani, "kdvOrani");
        foreach (var rental in ids)
            _ = await rentals.GetAsync(rental, ct); // şube kapsamı dışı → 403 (bulunamayan servis çıktısında "atlandı")

        var result = await invoices.BatchCreateFromRentalsAsync(ids, req.KdvOrani, ct);
        var cut = new List<DocumentResult>(result.Kesilen.Count);
        foreach (var fid in result.Kesilen) cut.Add(await ResultAsync(invoices, fid, ct));
        return TypedResults.Ok(new BatchInvoiceResult(cut, result.Atlananlar));
    }

    private static async Task<DocumentResult> ResultAsync(InvoiceService invoices, Guid id, CancellationToken ct)
        => new(id, (await invoices.GetAsync(id, ct))?.No ?? "");
}
