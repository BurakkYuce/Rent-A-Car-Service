using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// <c>/api/ui/v1/faturalar/*</c> (F8.1b) — Blazor <c>InvoiceList</c>, <c>InvoiceLineList</c>, <c>InvoicePrint</c>
/// karşılıkları. Kiradan fatura kesme F4.4'teki <c>POST finans/fatura</c>'dadır (tekrar yazılmadı); yazdırma tek
/// kaynak QuestPDF <c>/faturalar/{id}/pdf</c> (detayda <c>pdfAdresi</c>; yeni dosya ucu açılmadı).
/// <list type="bullet">
/// <item><b>Okuma:</b> FinanceWrite VEYA ViewReports; detay listesi ViewReports (servis guard'ı).</item>
/// <item><b>Manuel fatura</b> (E14): FinanceWrite, <c>Idempotency-Key</c> ZORUNLU. Önce bu anahtarla yazılmış fatura
/// aranır → 409 <c>mukerrer</c> + <c>mevcut{ayniIcerik}</c>; sonra servis.</item>
/// <item><b>İade faturası</b> (E17): FinanceReverse, yapısal (kaynak başına tek iade; ikinci 400).</item>
/// <item><b>Toplu</b> (E16): FinanceWrite; her kira kapsamdan geçer (biri dışarıdaysa HİÇBİRİ kesilmez, 403).</item>
/// <item><b>Kapsam:</b> faturanın kirası (fark faturasında kaynak kira, iadede kaynak faturanın kirası); kirasız
/// manuel fatura kiracı genelidir. Kapsam varlık/durum işinden ÖNCE.</item>
/// </list>
/// </summary>
public static partial class InvoiceUiApi
{
    public static RouteGroupBuilder MapInvoiceUiApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/faturalar").WithTags("Fatura");
        var read = g.MapGroup("").RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        read.MapGet("", List).AlanlariEsle(SortRules);
        read.MapGet("/ozet", Summary);
        read.MapGet("/{id:guid}", Detail);
        g.MapGet("/satirlar", Lines).RequirePermission(Permission.ViewReports).AlanlariEsle(SortRules);

        var write = g.MapGroup("").RequirePermission(Permission.FinanceWrite);
        write.MapPost("/manuel", CreateManual)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        write.MapPost("/toplu", CreateBatch);
        g.MapPost("/{id:guid}/iade", CreateRefund).RequirePermission(Permission.FinanceReverse);
        return g;
    }

    // ================================================================== okuma

    private static readonly SiralamaHaritasi<InvoiceListRow> ListSort = SiralamaHaritasi<InvoiceListRow>
        .Olustur(r => r.Id)
        .Alan("no", r => r.No).Alan("tarih", r => r.Tarih).Alan("vadeTarihi", r => r.VadeTarihi)
        .Alan("cariAd", r => r.CariAd).Alan("genelToplam", r => r.GenelToplam).Alan("durum", r => r.Durum)
        .Alan("doviz", r => r.Doviz);

    /// <summary>Blazor <c>InvoiceList</c> süzgeçleri. <c>iptal</c>: true yalnız iptaller, false iptal hariç.</summary>
    public sealed class InvoiceListFilter
    {
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "cariId")] public Guid? CariId { get; set; }
        [FromQuery(Name = "iptal")] public bool? Iptal { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
        [FromQuery(Name = "ofis")] public string? Ofis { get; set; }
        [FromQuery(Name = "doviz")] public string? Doviz { get; set; }
    }

    private static async Task<Ok<Sayfa<InvoiceListRow>>> List(
        [AsParameters] InvoiceListFilter f, InvoiceService invoices, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
        => TypedResults.Ok(F5Ortak.Sayfala(await ListRowsAsync(f, invoices, user, dbf, ct), ListSort, sayfa, boyut, sirala));

    /// <summary>
    /// Döviz bazında özet (#300; Blazor listesinin "Σ … TL · … USD" satırı). Liste ile AYNI süzgeçler ve AYNI küme
    /// (şube kapsamı, 500 belge ölçek sınırı) — sayfalamadan bağımsız, tüm eşleşen satırlar. Farklı dövizler TOPLANMAZ.
    /// <c>genelToplam</c> Blazor paritesi (iade faturası pozitif saklanır ve toplama girer); iadeler ayrıca
    /// <c>iadeAdet</c>/<c>iadeToplam</c> ile verilir — istemci çıkarma yapmaz.
    /// </summary>
    private static async Task<Ok<InvoiceSummary>> Summary(
        [AsParameters] InvoiceListFilter f, InvoiceService invoices, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var rows = await ListRowsAsync(f, invoices, user, dbf, ct);
        var totals = rows.GroupBy(r => r.Doviz)
            .OrderBy(g => g.Key == "TRY" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new InvoiceCurrencyTotal(g.Key, g.Count(), g.Sum(r => r.NetTutar), g.Sum(r => r.KdvTutar),
                g.Sum(r => r.GenelToplam), g.Count(r => r.IadeMi), g.Where(r => r.IadeMi).Sum(r => r.GenelToplam)))
            .ToList();
        return TypedResults.Ok(new InvoiceSummary(rows.Count, totals));
    }

    private static async Task<List<InvoiceListRow>> ListRowsAsync(
        InvoiceListFilter f, InvoiceService invoices, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        Text(f.Q, 128, "q");
        Text(f.Ofis, 128, "ofis");
        var (bas, bit) = F5Ortak.GunAraligi(f.Bas, f.Bit);
        var rows = await invoices.SearchAsync(new InvoiceFilter
        {
            Ara = F5Ortak.Nz(f.Q), CariId = f.CariId, Iptal = f.Iptal, Bas = bas, Bit = bit,
            Ofis = F5Ortak.Nz(f.Ofis), Doviz = string.IsNullOrWhiteSpace(f.Doviz) ? null : Currency(f.Doviz),
            EnFazla = 500, // ölçek sınırı: repo en yeni 500 belgeyi döner
        }, ct);

        await using var db = await dbf.CreateDbContextAsync(ct);
        var branches = await InvoiceBranchesAsync(db, rows.Select(r => r.Fatura).ToList(), ct);
        var visible = rows.Where(r => InScope(user, branches[r.Fatura.Id])).ToList();
        var names = await F5Ortak.CarilerAsync(dbf, visible.Select(r => r.Fatura.CariId), ct);
        return visible.Select(r => new InvoiceListRow(
            r.Fatura.Id, r.Fatura.No, r.Fatura.Tarih, r.Fatura.VadeTarihi, r.Fatura.Durum.ToString(), r.Fatura.IadeMi,
            r.Fatura.ManuelMi, r.Fatura.CariId, F5Ortak.CariAdi(names, r.Fatura.CariId),
            r.Fatura.RentalId ?? r.Fatura.KaynakKiraId, r.SozlesmeNo, r.Plaka, r.Ofis,
            r.Fatura.NetTutar, r.Fatura.KdvTutar, r.Fatura.GenelToplam, r.Fatura.Currency, r.Fatura.Kur,
            r.Fatura.EFaturaGonderildi, r.Fatura.EFaturaEttn, r.Fatura.KaynakFaturaId)).ToList();
    }

    private static async Task<Results<Ok<InvoiceDetail>, ProblemHttpResult>> Detail(
        Guid id, InvoiceService invoices, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var inv = await invoices.GetAsync(id, ct);
        if (inv is null) return F5Ortak.Bulunamadi("Fatura bulunamadı.");
        await using var db = await dbf.CreateDbContextAsync(ct);
        RequireInScope(user, (await InvoiceBranchesAsync(db, [inv], ct))[inv.Id]);

        var refundId = await db.Invoices.AsNoTracking().Where(i => i.KaynakFaturaId == inv.Id)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        var names = await F5Ortak.CarilerAsync(dbf, [inv.CariId], ct);
        return TypedResults.Ok(new InvoiceDetail(
            inv.Id, inv.No, inv.Tarih, inv.VadeTarihi, inv.Durum.ToString(), inv.IadeMi, inv.ManuelMi,
            inv.CariId, F5Ortak.CariAdi(names, inv.CariId), inv.RentalId ?? inv.KaynakKiraId, inv.KaynakFaturaId, refundId,
            inv.NetTutar, inv.KdvTutar, inv.GenelToplam, inv.Currency, inv.Kur,
            inv.Otv, inv.TevkifatOran, inv.TevkifatTutar, inv.DamgaVergisi,
            inv.IslemSube, inv.EvrakNo, inv.FaturaOzelKod, inv.OdemeTuru, inv.GonderimSekli, inv.KdvSifirSebep,
            inv.EFaturaGonderildi, inv.EFaturaEttn, $"/faturalar/{inv.Id}/pdf",
            inv.Lines.Select(l => new InvoiceLineDto(l.Id, l.Aciklama, l.Miktar, l.BirimNetFiyat, l.KdvOrani,
                l.SatirNet, l.SatirKdv, l.SatirToplam)).ToList()));
    }

    private static readonly SiralamaHaritasi<InvoiceLineListRow> LineSort = SiralamaHaritasi<InvoiceLineListRow>
        .Olustur(r => r.FaturaId)
        .Alan("faturaNo", r => r.FaturaNo).Alan("tarih", r => r.Tarih).Alan("cariAd", r => r.CariAd)
        .Alan("satirToplam", r => r.SatirToplam).Alan("kdvOrani", r => r.KdvOrani);

    public sealed class InvoiceLineFilter
    {
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "cariId")] public Guid? CariId { get; set; }
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        [FromQuery(Name = "ofis")] public string? Ofis { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
        [FromQuery(Name = "iptalleriGizle")] public bool? IptalleriGizle { get; set; }
    }

    private static async Task<Ok<Sayfa<InvoiceLineListRow>>> Lines(
        [AsParameters] InvoiceLineFilter f, InvoiceService invoices, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        Text(f.Q, 128, "q");
        Text(f.Plaka, 16, "plaka");
        var (bas, bit) = F5Ortak.GunAraligi(f.Bas, f.Bit);
        var rows = await invoices.ListLinesAsync(new FaturaSatirFilter
        {
            Ara = F5Ortak.Nz(f.Q), CariId = f.CariId, Plaka = F5Ortak.Nz(f.Plaka), Ofis = F5Ortak.Nz(f.Ofis),
            Bas = bas, Bit = bit, IptalleriGizle = f.IptalleriGizle ?? false, EnFazla = 2000, // ölçek sınırı
        }, ct);

        await using var db = await dbf.CreateDbContextAsync(ct);
        var faturaIds = rows.Select(r => r.FaturaId).Distinct().ToList();
        var heads = await db.Invoices.AsNoTracking().Where(i => faturaIds.Contains(i.Id)).ToListAsync(ct);
        var branches = await InvoiceBranchesAsync(db, heads, ct);
        var visible = rows.Where(r => branches.TryGetValue(r.FaturaId, out var b) && InScope(user, b)).ToList();
        var names = await F5Ortak.CarilerAsync(dbf, visible.Select(r => r.CariId), ct);
        var list = visible.Select(r => new InvoiceLineListRow(
            r.FaturaId, r.FaturaNo, r.Tarih, r.Durum.ToString(), r.IadeMi, r.ManuelMi, r.Doviz, r.Kur,
            r.CariId, F5Ortak.CariAdi(names, r.CariId), r.Aciklama, r.Miktar, r.BirimNetFiyat, r.KdvOrani,
            r.SatirNet, r.SatirKdv, r.SatirToplam, r.IsaretliToplamTl, r.RentalId, r.SozlesmeNo, r.Plaka,
            r.CikisOfisi)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, LineSort, sayfa, boyut, sirala));
    }

    /// <summary>Fatura → şube (kirası üzerinden). Kira: <c>RentalId</c> ?? fark faturasının <c>KaynakKiraId</c>;
    /// iade faturasında kaynak faturanınki. Kirasız (manuel) → bilinmiyor (kiracı geneli).</summary>
    internal static async Task<Dictionary<Guid, BranchInfo>> InvoiceBranchesAsync(
        AppDbContext db, IReadOnlyList<Invoice> invoices, CancellationToken ct)
    {
        var sourceIds = invoices.Where(i => i.KaynakFaturaId is not null).Select(i => i.KaynakFaturaId!.Value).Distinct().ToList();
        var sourceRentals = sourceIds.Count == 0 ? [] : await db.Invoices.AsNoTracking()
            .Where(i => sourceIds.Contains(i.Id))
            .Select(i => new { i.Id, Kira = i.RentalId ?? i.KaynakKiraId })
            .ToDictionaryAsync(i => i.Id, i => i.Kira, ct);
        Guid? RentalOf(Invoice i) => i.RentalId ?? i.KaynakKiraId
            ?? (i.KaynakFaturaId is { } k ? sourceRentals.GetValueOrDefault(k) : null);
        var rentals = await RentalBranchesAsync(db, invoices.Select(RentalOf).OfType<Guid>(), ct);
        return invoices.ToDictionary(i => i.Id,
            i => RentalOf(i) is { } r && rentals.TryGetValue(r, out var b) ? b : default);
    }
}
