using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.GelenEFaturalar;
using RentACar.Application.Integrations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Integrations;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// <c>/api/ui/v1/gelen-efatura/*</c> (F8.1b) — Blazor <c>GelenEFaturaList</c> karşılığı (create/onayla/reddet/işle/
/// bağla/giderleştir/sync). Hepsi FinanceWrite. Kiracı geneli tedarikçi belgesi: şubeye bağlı kullanıcı 403.
/// <list type="bullet">
/// <item><b>Sync — dürüst stub:</b> GİB entegrasyonu yapılandırılmadıysa (stub adaptör) sahte "0 eklendi" başarısı
/// DÖNMEZ: 400 "entegrasyon yapılandırılmadı".</item>
/// <item><b>Giderleştirme</b> (E24, TEK para yolu): deterministik anahtar <c>RowKey(faturaId, i)</c> (başlık
/// kullanılmaz); ikinci istek 409 <c>mukerrer</c>. Defter kümesi gider servisinin kümesidir (kopya yok).</item>
/// <item>Gövdedeki araç/cari/kategori kimlikleri kiracıda VAR olmalı (yetim bağ ya da yetim cari defteri yok).</item>
/// </list>
/// </summary>
public static class IncomingInvoiceUiApi
{
    public const string IntegrationMissing =
        "e-Fatura (GİB) entegrasyonu yapılandırılmadı; gelen kutusu çekilemedi. Faturaları elle girebilirsiniz.";

    public static RouteGroupBuilder MapIncomingInvoiceUiApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/gelen-efatura").WithTags("Gelen e-Fatura").RequirePermission(Permission.FinanceWrite);
        g.MapGet("", List).AlanlariEsle(SortRules);
        g.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create);
        g.MapPost("/sync", Sync);
        g.MapPost("/{id:guid}/onayla", (Guid id, GelenEFaturaService s, ICurrentUser u, CancellationToken ct)
            => TransitionAsync(id, s, u, () => s.OnaylaAsync(id, ct), GelenEFaturaDurum.Onaylandi, ct));
        g.MapPost("/{id:guid}/reddet", (Guid id, IncomingInvoiceRejectRequest req, GelenEFaturaService s, ICurrentUser u, CancellationToken ct) =>
        {
            Text(req.Neden, 512, "neden");
            return TransitionAsync(id, s, u, () => s.ReddetAsync(id, req.Neden, ct), GelenEFaturaDurum.Reddedildi, ct);
        });
        g.MapPost("/{id:guid}/isle", (Guid id, GelenEFaturaService s, ICurrentUser u, CancellationToken ct)
            => TransitionAsync(id, s, u, () => s.IsleAsync(id, ct), GelenEFaturaDurum.Islendi, ct));
        g.MapPut("/{id:guid}/bag", Link);
        g.MapPost("/{id:guid}/giderlestir", ToExpense)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        return g;
    }

    private static readonly SiralamaHaritasi<IncomingInvoiceRow> Sort = SiralamaHaritasi<IncomingInvoiceRow>
        .Olustur(r => r.Id)
        .Alan("tarih", r => r.Tarih).Alan("ettn", r => r.Ettn).Alan("gonderenUnvan", r => r.GonderenUnvan)
        .Alan("genelToplam", r => r.GenelToplam).Alan("durum", r => r.Durum);

    public sealed class IncomingInvoiceFilter
    {
        [FromQuery(Name = "firma")] public string? Firma { get; set; }
        [FromQuery(Name = "ettnBas")] public string? EttnBas { get; set; }
        [FromQuery(Name = "ettnBit")] public string? EttnBit { get; set; }
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        /// <summary>Beklemede | Onaylandi | Reddedildi | Islendi.</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
        [FromQuery(Name = "giderlestirildi")] public bool? Giderlestirildi { get; set; }
    }

    private static async Task<Ok<Sayfa<IncomingInvoiceRow>>> List(
        [AsParameters] IncomingInvoiceFilter f, GelenEFaturaService svc, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        RequireUnrestricted(user);
        Text(f.Firma, 256, "firma");
        Text(f.EttnBas, 64, "ettnBas");
        Text(f.EttnBit, 64, "ettnBit");
        Text(f.Plaka, 16, "plaka");
        var (bas, bit) = F5Ortak.GunAraligi(f.Bas, f.Bit);
        var rows = await svc.ListAsync(new GelenEFaturaFilter
        {
            Firma = F5Ortak.Nz(f.Firma), EttnBas = F5Ortak.Nz(f.EttnBas), EttnBit = F5Ortak.Nz(f.EttnBit),
            Plaka = F5Ortak.Nz(f.Plaka), Durum = F5Ortak.EnumAdi<GelenEFaturaDurum>(f.Durum, "durum"),
            Bas = bas, Bit = bit, Giderlestirildi = f.Giderlestirildi,
        }, ct);
        return TypedResults.Ok(F5Ortak.Sayfala(await RowsAsync(dbf, rows, ct), Sort, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<IncomingInvoiceDetail>, ProblemHttpResult>> Detail(
        Guid id, GelenEFaturaService svc, IGelenEFaturaRepository repo, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        RequireUnrestricted(user);
        var row = await svc.GetAsync(id, ct);
        if (row is null) return F5Ortak.Bulunamadi("Gelen fatura bulunamadı.");
        var version = await repo.VersionAsync(id, ct) ?? "";
        return TypedResults.Ok(new IncomingInvoiceDetail((await RowsAsync(dbf, [row], ct))[0], version));
    }

    private static async Task<List<IncomingInvoiceRow>> RowsAsync(
        IDbContextFactory<AppDbContext> dbf, IReadOnlyList<GelenEFatura> rows, CancellationToken ct)
    {
        var plates = await F5Ortak.PlakalarAsync(dbf, rows.Where(r => r.VehicleId is not null).Select(r => r.VehicleId!.Value), ct);
        var names = await F5Ortak.CarilerAsync(dbf, rows.Where(r => r.CariId is not null).Select(r => r.CariId!.Value), ct);
        return rows.Select(r => new IncomingInvoiceRow(
            r.Id, r.Ettn, r.GonderenVkn, r.GonderenUnvan, r.Tarih, r.NetTutar, r.KdvTutar, r.GenelToplam, r.Currency,
            r.Durum.ToString(), r.RedNedeni, r.Aciklama, r.Kdv20Matrah, r.Kdv20, r.Kdv10Matrah, r.Kdv10, r.Kdv1Matrah,
            r.Kdv1, r.Kdv0Matrah, r.VehicleId, r.VehicleId is { } v ? F5Ortak.Plaka(plates, v) : null,
            r.ExpenseCategoryId, r.CariId, r.CariId is { } c ? F5Ortak.CariAdi(names, c) : null, r.GiderTipi?.ToString(),
            r.GiderlestirilmeUtc is not null, r.GiderlestirilmeUtc)).ToList();
    }

    private static async Task<Ok<DocumentResult>> Create(
        IncomingInvoiceCreateRequest req, GelenEFaturaService svc, ICurrentUser user, CancellationToken ct)
    {
        RequireUnrestricted(user);
        if (string.IsNullOrWhiteSpace(req.Ettn)) throw new ValidationException("ETTN zorunludur.", "ettn");
        if (string.IsNullOrWhiteSpace(req.GonderenVkn)) throw new ValidationException("Gönderen VKN zorunludur.", "gonderenVkn");
        if (string.IsNullOrWhiteSpace(req.GonderenUnvan)) throw new ValidationException("Gönderen ünvanı zorunludur.", "gonderenUnvan");
        Text(req.Ettn.Trim(), 64, "ettn");
        Text(req.GonderenVkn.Trim(), 16, "gonderenVkn");
        Text(req.GonderenUnvan.Trim(), 256, "gonderenUnvan");
        Text(req.Aciklama, 512, "aciklama");
        Amount(req.GenelToplam, "genelToplam");
        AmountLimit(req.NetTutar, "netTutar");
        AmountLimit(req.KdvTutar, "kdvTutar");
        if (req.NetTutar < 0m) throw new ValidationException("Net tutar negatif olamaz.", "netTutar");
        if (req.KdvTutar < 0m) throw new ValidationException("KDV tutarı negatif olamaz.", "kdvTutar");
        WithField("tarih", () => TarihPolitikasi.ParaTarihi(req.Tarih, "Fatura"));
        var id = await svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = req.Ettn.Trim(), GonderenVkn = req.GonderenVkn.Trim(), GonderenUnvan = req.GonderenUnvan.Trim(),
            Tarih = F5Ortak.Utc(req.Tarih), NetTutar = req.NetTutar, KdvTutar = req.KdvTutar,
            GenelToplam = req.GenelToplam, Currency = Currency(req.Doviz), Aciklama = Trimmed(req.Aciklama),
        }, ct);
        return TypedResults.Ok(new DocumentResult(id, req.Ettn.Trim()));
    }

    private static async Task<Ok<IncomingInvoiceSyncResult>> Sync(
        IncomingInvoiceSyncRequest req, GelenEFaturaService svc, IEInvoiceService einvoice, ICurrentUser user, CancellationToken ct)
    {
        RequireUnrestricted(user);
        if (req.Bit < req.Bas) throw new ValidationException("Bitiş tarihi başlangıçtan önce olamaz.", "bit");
        if (req.Bit.DayNumber - req.Bas.DayNumber > 92) throw new ValidationException("En çok 92 günlük aralık çekilebilir.", "bit");
        // Dürüst stub: yapılandırılmamış entegrasyon "0 yeni fatura" diye BAŞARI göstermez.
        if (einvoice is StubEInvoiceService) throw new ValidationException(IntegrationMissing);
        var (from, to) = F5Ortak.GunAraligi(req.Bas, req.Bit);
        return TypedResults.Ok(new IncomingInvoiceSyncResult(await svc.SyncFromGibAsync(from!.Value, to!.Value, ct)));
    }

    private static async Task<Results<Ok<IncomingInvoiceStateResult>, ProblemHttpResult>> TransitionAsync(
        Guid id, GelenEFaturaService svc, ICurrentUser user, Func<Task<bool>> act, GelenEFaturaDurum target, CancellationToken ct)
    {
        RequireUnrestricted(user);
        if (await svc.GetAsync(id, ct) is null) return F5Ortak.Bulunamadi("Gelen fatura bulunamadı.");
        await act();
        return TypedResults.Ok(new IncomingInvoiceStateResult(id, target.ToString()));
    }

    private static async Task<Results<Ok<IncomingInvoiceLinkResult>, ProblemHttpResult>> Link(
        Guid id, IncomingInvoiceLinkRequest req, GelenEFaturaService svc, IGelenEFaturaRepository repo, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        RequireUnrestricted(user);
        // #286 M3: tam değiştirme PUT'u iyimser eşzamanlılık ister — bayat sekme başkasının kırılımını ezmesin.
        if (string.IsNullOrWhiteSpace(req.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        foreach (var (v, f) in new[] { (req.Kdv20Matrah, "kdv20Matrah"), (req.Kdv20, "kdv20"), (req.Kdv10Matrah, "kdv10Matrah"),
                     (req.Kdv10, "kdv10"), (req.Kdv1Matrah, "kdv1Matrah"), (req.Kdv1, "kdv1"), (req.Kdv0Matrah, "kdv0Matrah") })
        {
            AmountLimit(v, f);
            if (v < 0m) throw new ValidationException("Tutar negatif olamaz.", f);
        }
        var tip = F5Ortak.EnumAdi<ExpenseType>(req.GiderTipi, "giderTipi");
        var row = await svc.GetAsync(id, ct);
        if (row is null) return F5Ortak.Bulunamadi("Gelen fatura bulunamadı.");
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            await RequireVehicleAsync(db, req.AracId, "aracId", ct);
            await RequireCustomerAsync(db, req.CariId, "cariId", ct);
            if (req.GiderKategoriId is { } k && !await db.ExpenseCategories.AsNoTracking().AnyAsync(x => x.Id == k, ct))
                throw new ValidationException("Gider kategorisi bulunamadı.", "giderKategoriId");
        }
        await svc.BaglaAsync(new GelenEFaturaBaglamaInput
        {
            Id = id, Kdv20Matrah = req.Kdv20Matrah, Kdv20 = req.Kdv20, Kdv10Matrah = req.Kdv10Matrah, Kdv10 = req.Kdv10,
            Kdv1Matrah = req.Kdv1Matrah, Kdv1 = req.Kdv1, Kdv0Matrah = req.Kdv0Matrah, VehicleId = req.AracId,
            ExpenseCategoryId = req.GiderKategoriId, CariId = req.CariId, GiderTipi = tip,
        }, ct, expectedVersion: req.Surum);
        var after = await svc.GetAsync(id, ct);
        return TypedResults.Ok(new IncomingInvoiceLinkResult(id, (after ?? row).Durum.ToString(),
            await repo.VersionAsync(id, ct) ?? ""));
    }

    private static async Task<Results<Ok<IncomingInvoiceExpenseResult>, ProblemHttpResult>> ToExpense(
        Guid id, IncomingInvoiceExpenseRequest req, GelenEFaturaService svc, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        RequireUnrestricted(user);
        var method = F5Ortak.EnumAdi<OdemeYontemi>(req.OdemeYontemi, "odemeYontemi") ?? OdemeYontemi.AcikHesap;
        Text(req.Sube, 64, "sube");
        var row = await svc.GetAsync(id, ct);
        if (row is null) return F5Ortak.Bulunamadi("Gelen fatura bulunamadı.");
        await using (var db = await dbf.CreateDbContextAsync(ct))
            await RequireCustomerAsync(db, method == OdemeYontemi.AcikHesap ? req.CariId ?? row.CariId : null, "cariId", ct);
        WithField("doviz", () => Currency(row.Currency)); // eski "TL"/etiket belge: defter ISO koda indirger (N1)
        var count = await svc.GiderlestirAsync(new GelenEFaturaGiderInput
        {
            Id = id, OdemeYontemi = method, CariId = req.CariId, Sube = Trimmed(req.Sube),
        }, ct);
        return TypedResults.Ok(new IncomingInvoiceExpenseResult(id, count));
    }
}
