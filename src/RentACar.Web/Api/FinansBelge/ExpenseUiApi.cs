using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// <c>/api/ui/v1/giderler/*</c> (F8.1b) — Blazor <c>ExpenseList</c> karşılığı.
/// <list type="bullet">
/// <item><b>İzin:</b> okuma FinanceWrite VEYA ViewReports; yazma FinanceWrite (Blazor grubu gibi).</item>
/// <item><b>Kapsam:</b> giderin şubesi (servis listesi ve tekil okuma kapsamı zaten uygular → tekilde 403).
/// Yazmada şubeli kullanıcı kendi şubesini vermeli; araç/kira verilirse onlar da kapsamda olmalı.</item>
/// <item><b>Gider</b> (E21): <c>Idempotency-Key</c> ZORUNLU; önce bu anahtarla yazılmış gider (409 + mevcut).
/// Borç Gider(net) + Borç KDV / Alacak Kasa·Banka·Cari(brüt); KDV satır bazında kuruşa yuvarlanır; döviz ISO koda
/// indirgenir ("TL" → TRY, #279 N1); kur KurCozucu'dan; dönem kilidi serviste.</item>
/// <item><b>Ödeme takibi</b> (E23, deftere yazmaz): başlık ZORUNLU; önce anahtar (409 + mevcut), sonra servis
/// (gider başına danışma kilidi altında kalan).</item>
/// </list>
/// </summary>
public static class ExpenseUiApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string ExpenseAlreadySaved = "Bu gider zaten kaydedildi (No {0}, {1} {2}); yeni gider yazılmadı.";
    public const string ExpenseOtherSaved =
        "Bu işlem anahtarıyla başka bir gider yazılmış (No {0}, {1} {2}); girdiğiniz gider YAZILMADI. Kayıtları kontrol edin.";
    public const string PaymentAlreadySaved = "Bu gider ödemesi zaten kaydedildi ({0} {1}); yeni ödeme yazılmadı.";
    public const string PaymentOtherSaved =
        "Bu işlem anahtarıyla başka bir gider ödemesi yazılmış ({0} {1}); girdiğiniz ödeme YAZILMADI. Kayıtları kontrol edin.";

    public static RouteGroupBuilder MapExpenseUiApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/giderler").WithTags("Gider");
        var read = g.MapGroup("").RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        read.MapGet("", List).AlanlariEsle(SortRules);
        read.MapGet("/{id:guid}", Detail);
        var write = g.MapGroup("").RequirePermission(Permission.FinanceWrite);
        write.MapPost("", Create).Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        write.MapPost("/{id:guid}/odeme", Pay).Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        return g;
    }

    // ================================================================== okuma

    private static readonly SiralamaHaritasi<ExpenseListRow> Sort = SiralamaHaritasi<ExpenseListRow>
        .Olustur(r => r.Id)
        .Alan("no", r => r.No).Alan("tarih", r => r.Tarih).Alan("tip", r => r.Tip).Alan("plaka", r => r.Plaka)
        .Alan("cariAd", r => r.CariAd).Alan("genelToplam", r => r.GenelToplam).Alan("kalan", r => r.Kalan)
        .Alan("sube", r => r.Sube);

    public sealed class ExpenseListFilter
    {
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "cariId")] public Guid? CariId { get; set; }
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        [FromQuery(Name = "tip")] public string? Tip { get; set; }
        [FromQuery(Name = "sube")] public string? Sube { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
    }

    private static async Task<Ok<Sayfa<ExpenseListRow>>> List(
        [AsParameters] ExpenseListFilter f, ExpenseService expenses, IDbContextFactory<AppDbContext> dbf,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        Text(f.Q, 128, "q");
        Text(f.Plaka, 16, "plaka");
        var (bas, bit) = F5Ortak.GunAraligi(f.Bas, f.Bit);
        var rows = await expenses.ListAsync(new ExpenseFilter // şube kapsamı serviste HER ZAMAN uygulanır
        {
            Ara = F5Ortak.Nz(f.Q), CariId = f.CariId, Plaka = F5Ortak.Nz(f.Plaka),
            Tip = F5Ortak.EnumAdi<ExpenseType>(f.Tip, "tip"), Sube = F5Ortak.Nz(f.Sube), Bas = bas, Bit = bit,
        }, ct);
        var list = await RowsAsync(expenses, dbf, rows, ct);
        return TypedResults.Ok(F5Ortak.Sayfala(list, Sort, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<ExpenseDetail>, ProblemHttpResult>> Detail(
        Guid id, ExpenseService expenses, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var e = await expenses.GetAsync(id, ct); // kapsam dışı → 403 (servis)
        if (e is null) return F5Ortak.Bulunamadi("Gider bulunamadı.");
        var row = (await RowsAsync(expenses, dbf, [e], ct))[0];
        await using var db = await dbf.CreateDbContextAsync(ct);
        var payments = await db.GiderOdemeleri.AsNoTracking().Where(o => o.ExpenseId == id).OrderBy(o => o.Sira)
            .Select(o => new ExpensePaymentDto(o.Id, o.Sira, o.Tutar, o.KalanSonrasi, o.Tarih, o.MakbuzNo, o.Aciklama, o.IslemYapan))
            .ToListAsync(ct);
        return TypedResults.Ok(new ExpenseDetail(row, e.HazirAciklama, e.FinansalHesapId, payments));
    }

    private static async Task<List<ExpenseListRow>> RowsAsync(
        ExpenseService expenses, IDbContextFactory<AppDbContext> dbf, IReadOnlyList<Expense> rows, CancellationToken ct)
    {
        var status = await expenses.OdemeDurumlariAsync(rows.ToList(), ct);
        var plates = await F5Ortak.PlakalarAsync(dbf, rows.Where(e => e.VehicleId is not null).Select(e => e.VehicleId!.Value), ct);
        var names = await F5Ortak.CarilerAsync(dbf, rows.Where(e => e.CariId is not null).Select(e => e.CariId!.Value), ct);
        var contracts = await ContractNumbersAsync(dbf, rows.Where(e => e.RentalId is not null).Select(e => e.RentalId!.Value), ct);
        return rows.Select(e =>
        {
            var s = status.GetValueOrDefault(e.Id);
            return new ExpenseListRow(
                e.Id, e.No, e.Tip.ToString(), e.Tarih, e.VehicleId, e.VehicleId is { } v ? F5Ortak.Plaka(plates, v) : null,
                e.CariId, e.CariId is { } c ? F5Ortak.CariAdi(names, c) : null, e.Sube, e.EvrakNo, e.NetTutar, e.KdvOrani,
                e.KdvTutar, e.GenelToplam, e.Currency, e.Kur, e.OdemeYontemi.ToString(), e.KasaBankaHesap.ToString(),
                e.Aciklama, e.RentalId, e.Vade, e.OdemeTarihi, s?.Odenen ?? e.GenelToplam, s?.Kalan ?? 0m, s?.TakipEdilir ?? false,
                e.RentalId is { } r ? contracts.GetValueOrDefault(r) : null);
        }).ToList();
    }

    /// <summary>Kira → sözleşme no (gider listesinin "Sözleşme" sütunu; #300 parite farkı). Yalnız numara döner; gider
    /// satırı zaten giderin şube kapsamından geçmiştir.</summary>
    private static async Task<Dictionary<Guid, string>> ContractNumbersAsync(
        IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking().Where(r => list.Contains(r.Id))
            .Select(r => new { r.Id, r.SozlesmeNo }).ToDictionaryAsync(r => r.Id, r => r.SozlesmeNo, ct);
    }

    // ================================================================== yazma

    private static async Task<Ok<DocumentResult>> Create(
        ExpenseCreateRequest req, HttpContext http, ExpenseService expenses, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        var tip = F5Ortak.EnumAdi<ExpenseType>(req.Tip, "tip") ?? ExpenseType.Genel;
        var method = F5Ortak.EnumAdi<OdemeYontemi>(req.OdemeYontemi, "odemeYontemi")
                     ?? throw new ValidationException("Ödeme yöntemi seçilmelidir (Nakit, Banka, AcikHesap).", "odemeYontemi");
        Amount(req.NetTutar, "netTutar");
        VatRate(req.KdvOrani, "kdvOrani");
        var currency = Currency(req.Doviz);
        Rate(req.Kur);
        BaseLimit(req.NetTutar * (1m + req.KdvOrani), req.Kur, "netTutar");
        Text(req.Sube, 64, "sube");
        Text(req.EvrakNo, 64, "evrakNo");
        Text(req.Aciklama, 512, "aciklama");
        Text(req.HazirAciklama, 512, "hazirAciklama");
        if (method == OdemeYontemi.AcikHesap && req.CariId is null)
            throw new ValidationException("Açık hesap (tedarikçi) gideri için cari seçilmelidir.", "cariId");
        if (tip == ExpenseType.Arac && req.AracId is null)
            throw new ValidationException("Araç gideri için araç seçilmelidir.", "aracId");
        WithField("tarih", () => TarihPolitikasi.ParaTarihi(req.Tarih, "Gider"));

        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            // Kapsam: şubeli kullanıcı yalnız kendi şubesine gider yazar (kendisinin göremeyeceği gider açılmasın).
            if (IsRestricted(user))
            {
                if (Trimmed(req.Sube) is null)
                    throw new ValidationException("Şubeye bağlı kullanıcı gideri kendi şubesine yazmalıdır.", "sube");
                RequireInScope(user, new BranchInfo(true, null, req.Sube!.Trim()));
            }
            await RequireCustomerAsync(db, req.CariId, "cariId", ct);
            if (await RequireVehicleAsync(db, req.AracId, "aracId", ct) is { } vb) RequireInScope(user, vb);
            if (await RequireRentalAsync(db, req.KiraId, "kiraId", ct) is { } rb) RequireInScope(user, rb);

            if (await db.Expenses.AsNoTracking().FirstOrDefaultAsync(x => x.IslemAnahtari == key, ct) is { } existing)
            {
                var (kdv, gross) = KdvMath.FromNet(req.NetTutar, req.KdvOrani);
                var same = existing.GenelToplam == gross && existing.KdvTutar == kdv && existing.Currency == currency
                           && existing.OdemeYontemi == method && existing.Tip == tip && existing.CariId == req.CariId
                           && existing.VehicleId == req.AracId;
                var amount = existing.GenelToplam.ToString("N2", Tr);
                throw new MukerrerIslemException(
                    string.Format(Tr, same ? ExpenseAlreadySaved : ExpenseOtherSaved, existing.No, amount, existing.Currency),
                    new MevcutIslem(existing.Id, existing.No, existing.GenelToplam, existing.Currency, same));
            }
        }

        var id = await expenses.CreateAsync(new ExpenseInput
        {
            Tip = tip, Tarih = F5Ortak.Utc(req.Tarih), VehicleId = req.AracId, CariId = req.CariId,
            Sube = Trimmed(req.Sube), EvrakNo = Trimmed(req.EvrakNo), NetTutar = req.NetTutar, KdvOrani = req.KdvOrani,
            OdemeYontemi = method, Doviz = currency, Kur = req.Kur, Aciklama = Trimmed(req.Aciklama),
            FinansalHesapId = req.HesapId, OdemeTarihi = F5Ortak.Utc(req.OdemeTarihi),
            HazirAciklama = Trimmed(req.HazirAciklama), RentalId = req.KiraId, Vade = F5Ortak.Utc(req.Vade),
            IslemAnahtari = key,
        }, ct);
        return TypedResults.Ok(new DocumentResult(id, (await expenses.GetAsync(id, ct))?.No ?? ""));
    }

    private static async Task<Results<Ok<ExpensePaymentDto>, ProblemHttpResult>> Pay(
        Guid id, ExpensePaymentRequest req, HttpContext http, ExpenseService expenses,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        OptionalAmount(req.Tutar, "tutar");
        Text(req.MakbuzNo, 32, "makbuzNo");
        Text(req.Aciklama, 512, "aciklama");
        var e = await expenses.GetAsync(id, ct); // kapsam dışı → 403 (durumdan önce)
        if (e is null) return F5Ortak.Bulunamadi("Gider bulunamadı.");

        await using (var db = await dbf.CreateDbContextAsync(ct))
            if (await db.GiderOdemeleri.AsNoTracking().FirstOrDefaultAsync(o => o.IslemAnahtari == key, ct) is { } o)
            {
                var same = o.ExpenseId == id && (req.Tutar is not { } t || decimal.Round(t, 2, MidpointRounding.ToZero) == o.Tutar);
                var amount = o.Tutar.ToString("N2", Tr);
                throw new MukerrerIslemException(
                    string.Format(Tr, same ? PaymentAlreadySaved : PaymentOtherSaved, amount, e.Currency),
                    o.ExpenseId == id ? new MevcutIslem(o.Id, $"{e.No}/{o.Sira}", o.Tutar, e.Currency, same) : null);
            }
        WithField("tarih", () => TarihPolitikasi.ParaTarihi(req.Tarih, "Gider ödemesi"));

        var p = await expenses.OdemeEkleAsync(new GiderOdemeInput
        {
            ExpenseId = id, Tutar = req.Tutar, Tarih = F5Ortak.Utc(req.Tarih), MakbuzNo = Trimmed(req.MakbuzNo),
            Aciklama = Trimmed(req.Aciklama), IslemAnahtari = key,
        }, ct);
        // Yarışı kaybeden aynı anahtar: servis sessiz null döner — yeni SPA için bu da mükerrerdir (yazılmadı).
        if (p is null) throw new MukerrerIslemException("Bu gider ödemesi zaten kaydedildi; yeni ödeme yazılmadı.");
        return TypedResults.Ok(new ExpensePaymentDto(p.Id, p.Sira, p.Tutar, p.KalanSonrasi, p.Tarih, p.MakbuzNo, p.Aciklama, p.IslemYapan));
    }
}
