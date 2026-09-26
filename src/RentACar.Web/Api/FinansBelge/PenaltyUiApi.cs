using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Penalties;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// <c>/api/ui/v1/cezalar/*</c> (F8.1b) — Blazor <c>PenaltyList</c> karşılığı (create/yansıt/kısmi öde/iptal).
/// <list type="bullet">
/// <item><b>İzin:</b> okuma OperationsWrite VEYA FinanceWrite VEYA ViewReports; kayıt OperationsWrite; yansıtma ve
/// ödeme (defter yazar) FinanceWrite; iptal OperationsDelete — Blazor uçlarıyla aynı.</item>
/// <item><b>Kapsam:</b> cezanın kirası, yoksa aracı (Penalty.IslemSube bilinçli olarak kapsam DEĞİL). Kirasız ve
/// araçsız ceza kiracı genelidir. Başka şube 403, olmayan/başka kiracı 404; kapsam durumdan ÖNCE.</item>
/// <item><b>Yansıtma</b> (E25, yapısal): FOR UPDATE altında yalnız 'Yeni' → ikinci 400. Borç Cari / Alacak Gelir.</item>
/// <item><b>Ödeme</b> (E26): <c>Idempotency-Key</c> ZORUNLU; önce bu anahtarla yazılmış ödeme aranır (409 +
/// mevcut), sonra servis (tarih/dönem kilidi, danışma kilidi altında kalemin kalanı). Borç Gider / Alacak
/// Kasa-Banka. Yansıtma (gelir tarafı) ile ödeme (maliyet tarafı) aynı cezada net 0 — çift sayım değil.</item>
/// </list>
/// </summary>
public static class PenaltyUiApi
{
    public static RouteGroupBuilder MapPenaltyUiApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/cezalar").WithTags("Ceza");
        var read = g.MapGroup("").RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        read.MapGet("", List).AlanlariEsle(SortRules);
        read.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/yansit", Reflect).RequirePermission(Permission.FinanceWrite);
        g.MapPost("/{id:guid}/odeme", Pay).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    // ================================================================== okuma

    private static readonly SortFieldMap<PenaltyListRow> Sort = SortFieldMap<PenaltyListRow>
        .Create(r => r.Id)
        .Alan("no", r => r.No).Alan("tebligTarihi", r => r.TebligTarihi).Alan("vadeTarihi", r => r.VadeTarihi)
        .Alan("tutar", r => r.Tutar).Alan("kalan", r => r.Kalan).Alan("durum", r => r.Durum)
        .Alan("plaka", r => r.Plaka).Alan("cariAd", r => r.CariAd);

    public sealed class PenaltyListFilter
    {
        /// <summary>Müşteri no / ad / e-posta içinde.</summary>
        [FromQuery(Name = "musteri")] public string? Musteri { get; set; }
        [FromQuery(Name = "makbuzNo")] public string? MakbuzNo { get; set; }
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
        /// <summary>Yeni | Yansitildi | Odendi | Iptal | Kismi.</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        /// <summary>Odenmemis | Kismi | Odendi.</summary>
        [FromQuery(Name = "odemeDurumu")] public string? OdemeDurumu { get; set; }
        [FromQuery(Name = "islemSube")] public string? IslemSube { get; set; }
    }

    private static async Task<Ok<Sayfa<PenaltyListRow>>> List(
        [AsParameters] PenaltyListFilter f, PenaltyService penalties, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        Text(f.Musteri, 128, "musteri");
        Text(f.MakbuzNo, 64, "makbuzNo");
        Text(f.Plaka, 16, "plaka");
        var rows = await penalties.ListRowsAsync(new PenaltyFilter
        {
            Musteri = F5Ortak.Nz(f.Musteri), MakbuzNo = F5Ortak.Nz(f.MakbuzNo), Plaka = F5Ortak.Nz(f.Plaka),
            // Repo sözleşmesi: Bit = bitiş gününün başlangıcı (repo +1 gün uygular). İstanbul günü.
            Bas = f.Bas is { } b ? F5Ortak.GunBasi(b) : null, Bit = f.Bit is { } t ? F5Ortak.GunBasi(t) : null,
            Durum = F5Ortak.EnumAdi<PenaltyStatus>(f.Durum, "durum"),
            OdemeDurum = F5Ortak.EnumAdi<PenaltyPaymentStatus>(f.OdemeDurumu, "odemeDurumu"),
            IslemSube = F5Ortak.Nz(f.IslemSube),
        }, ct);

        await using var db = await dbf.CreateDbContextAsync(ct);
        var branches = await BranchesAsync(db, rows.Select(r => r.Ceza).ToList(), ct);
        var visible = rows.Where(r => InScope(user, branches[r.Ceza.Id])).ToList();
        var names = await F5Ortak.CarilerAsync(dbf, visible.Where(r => r.Ceza.CariId is not null).Select(r => r.Ceza.CariId!.Value), ct);
        var list = visible.Select(r => Row(r.Ceza, r.Plaka, names, r.SozlesmeNo, r.FaturaNo)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, Sort, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<PenaltyDetail>, ProblemHttpResult>> Detail(
        Guid id, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (await LoadInScopeAsync(db, penalties, user, id, ct) is not { } p) return F5Ortak.Bulunamadi("Ceza bulunamadı.");
        var plaka = p.VehicleId is { } v ? await db.Vehicles.AsNoTracking().Where(x => x.Id == v).Select(x => x.Plaka).FirstOrDefaultAsync(ct) : null;
        var soz = p.RentalId is { } r ? await db.Rentals.AsNoTracking().Where(x => x.Id == r).Select(x => x.SozlesmeNo).FirstOrDefaultAsync(ct) : null;
        var fatura = p.RentalId is { } r2 ? await db.Invoices.AsNoTracking().Where(i => i.RentalId == r2)
            .OrderBy(i => i.Tarih).Select(i => i.No).FirstOrDefaultAsync(ct) : null;
        var names = await F5Ortak.CarilerAsync(dbf, p.CariId is { } c ? [c] : [], ct);
        var lines = await penalties.ListLinesAsync(id, ct);
        var payments = await penalties.ListPaymentsAsync(id, ct);
        // #286 Low-7: ihbarname telefonu müşterinin telefonudur — müşteri telefonu anonimleştirildiyse (KVKK,
        // MusteriGorunumu kuralı) bu kopya da dönmez.
        var phoneHidden = p.CariId is { } pc && await db.Customers.AsNoTracking()
            .AnyAsync(x => x.Id == pc && x.AnonimTelefon, ct);
        return TypedResults.Ok(new PenaltyDetail(Row(p, plaka, names, soz, fatura), phoneHidden ? null : p.CepTel,
            lines.Select(s => new PenaltyLineDto(s.Id, s.Sira, s.Tutar, s.Odenen, s.Kalan, s.Sebep)).ToList(),
            payments.Select(o => new PenaltyPaymentDto(o.Id, o.SatirId, o.Sira, o.Tutar, o.Tarih, o.Hesap.ToString(),
                o.KalanSonrasi, o.MakbuzNo, o.KasaKodu, o.HesapNo, o.IslemYapan, o.Aciklama)).ToList()));
    }

    private static PenaltyListRow Row(Penalty p, string? plaka, Dictionary<Guid, F5Ortak.CariGorunum> names,
        string? sozlesmeNo, string? faturaNo) => new(
        p.Id, p.No, p.CezaTuru, p.TebligTarihi, p.VadeTarihi, p.Durum.ToString(), p.Tutar, p.OdenenTutar, p.Kalan,
        (p.OdenenTutar <= 0m ? PenaltyPaymentStatus.Odenmemis : p.Kalan > 0m ? PenaltyPaymentStatus.Kismi : PenaltyPaymentStatus.Odendi).ToString(),
        p.Sebep, p.VehicleId, plaka, p.CariId, p.CariId is { } c ? F5Ortak.CariAdi(names, c) : null,
        p.RentalId, sozlesmeNo, faturaNo, p.MakbuzNo, p.IslemSube, p.Yer, p.Saat, p.OdenmeTarihi);

    // ================================================================== kapsam

    /// <summary>Ceza → şube: kirası, yoksa aracı; ikisi de yoksa bilinmiyor (kiracı geneli).</summary>
    internal static async Task<Dictionary<Guid, BranchInfo>> BranchesAsync(
        AppDbContext db, IReadOnlyList<Penalty> list, CancellationToken ct)
    {
        var rentals = await RentalBranchesAsync(db, list.Where(p => p.RentalId is not null).Select(p => p.RentalId!.Value), ct);
        var vehicles = await VehicleBranchesAsync(db, list.Where(p => p.RentalId is null && p.VehicleId is not null)
            .Select(p => p.VehicleId!.Value), ct);
        return list.ToDictionary(p => p.Id, p =>
            p.RentalId is { } r ? rentals.GetValueOrDefault(r)
            : p.VehicleId is { } v ? vehicles.GetValueOrDefault(v) : default);
    }

    /// <summary>Ceza var mı (yoksa null → 404) ve kapsamda mı (değilse 403). Durum işinden ÖNCE çağrılır.</summary>
    internal static async Task<Penalty?> LoadInScopeAsync(
        AppDbContext db, PenaltyService penalties, ICurrentUser user, Guid id, CancellationToken ct)
    {
        var p = await penalties.GetAsync(id, ct);
        if (p is null) return null;
        RequireInScope(user, (await BranchesAsync(db, [p], ct))[p.Id]);
        return p;
    }

    // ================================================================== yazma

    private static Task<Results<Ok<DocumentResult>, ProblemHttpResult>> Create(
        PenaltyCreateRequest req, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct) => PenaltyWrites.CreateAsync(req, penalties, user, dbf, ct);

    private static Task<Results<Ok<PenaltyStateResult>, ProblemHttpResult>> Reflect(
        Guid id, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => PenaltyWrites.ReflectAsync(id, penalties, user, dbf, ct);

    private static Task<Results<Ok<PenaltyPaymentResult>, ProblemHttpResult>> Pay(
        Guid id, PenaltyPaymentRequest req, HttpContext http, PenaltyService penalties, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct) => PenaltyWrites.PayAsync(id, req, http, penalties, user, dbf, ct);

    private static Task<Results<Ok<PenaltyStateResult>, ProblemHttpResult>> Cancel(
        Guid id, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => PenaltyWrites.CancelAsync(id, penalties, user, dbf, ct);
}
