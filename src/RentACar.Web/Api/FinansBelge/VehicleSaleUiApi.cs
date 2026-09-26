using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>Araç satış satırı. Tutarlar satışın dövizinde. Alıcı adı KVKK tek kuralıyla.</summary>
public sealed record VehicleSaleRow(
    Guid Id, string No, DateTimeOffset Tarih, Guid AracId, string Plaka, Guid AliciCariId, string AliciAd,
    decimal SatisNet, decimal KdvOrani, decimal KdvTutar, decimal GenelToplam, string Doviz, decimal Kur, string Durum,
    string? NoterNo, DateTimeOffset? NoterSatisTarihi, DateTimeOffset? IhaleTarihi, string? IhaleFirmasi,
    int? SatisKm, string? SatisKanali, bool SatisiVerildi, string? Aciklama);

/// <summary>Araç satışı. <c>kdvOrani</c> KESİR (0,20 = %20). <c>doviz</c> boş → TRY ("TL" → TRY); <c>kur</c> boş →
/// otomatik. Bilgi alanları deftere YANSIMAZ.</summary>
public sealed record VehicleSaleCreateRequest(
    Guid AracId, Guid AliciCariId, decimal SatisNet, decimal KdvOrani,
    DateTimeOffset? Tarih = null, string? Doviz = null, decimal? Kur = null, string? NoterNo = null,
    string? Aciklama = null, decimal? HedefFiyat = null, int? SatisKm = null, string? SatisKanali = null,
    string? Devir = null, DateTimeOffset? IhaleTarihi = null, string? IhaleFirmasi = null,
    DateTimeOffset? NoterSatisTarihi = null, bool KirayaVerme = false, int? IlanKm = null, string? ListeDoviz = null,
    string? SatisNoktasi = null, string? UygulananKampanya = null, string? IhaleSayisi = null,
    bool SatisiVerildi = false, string? YevmiyeNumarasi = null, string? Aciklama2 = null);

/// <summary>
/// <c>/api/ui/v1/satislar/*</c> (F8.1b) — Blazor <c>VehicleSaleList</c> karşılığı.
/// <list type="bullet">
/// <item><b>İzin:</b> okuma FinanceWrite VEYA ViewReports VEYA OperationsWrite (servis <c>SearchAsync</c> ile aynı);
/// satış FinanceWrite.</item>
/// <item><b>Kapsam:</b> satılan aracın şubesi; liste süzülür, satışta araç kapsamda olmalı (403).</item>
/// <item><b>Satış</b> (E35, yapısal): araç başına tek tamamlanmış satış (kilit + kısmi unique) → ikinci 400.
/// Başlık kullanılmaz. Borç Cari(brüt) / Alacak Gelir(net) + KDV; KDV satır bazında kuruşa yuvarlanır; boşluksuz
/// belge no aynı transaction'da; dönem kilidi serviste.</item>
/// </list>
/// </summary>
public static class VehicleSaleUiApi
{
    public static RouteGroupBuilder MapVehicleSaleUiApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/satislar").WithTags("Araç Satış");
        g.MapGet("", List).RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports, Permission.OperationsWrite)
            .AlanlariEsle(SortRules);
        g.MapPost("", Create).RequirePermission(Permission.FinanceWrite);
        return g;
    }

    private static readonly SortFieldMap<VehicleSaleRow> Sort = SortFieldMap<VehicleSaleRow>
        .Create(r => r.Id)
        .Alan("no", r => r.No).Alan("tarih", r => r.Tarih).Alan("plaka", r => r.Plaka).Alan("aliciAd", r => r.AliciAd)
        .Alan("genelToplam", r => r.GenelToplam);

    public sealed class VehicleSaleFilterQuery
    {
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        [FromQuery(Name = "aliciCariId")] public Guid? AliciCariId { get; set; }
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "satisiVerildi")] public bool? SatisiVerildi { get; set; }
        [FromQuery(Name = "ofis")] public string? Ofis { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }
    }

    private static async Task<Ok<Sayfa<VehicleSaleRow>>> List(
        [AsParameters] VehicleSaleFilterQuery f, VehicleSaleService sales, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        Text(f.Plaka, 16, "plaka");
        var (bas, bit) = F5Ortak.GunAraligi(f.Bas, f.Bit);
        var rows = await sales.SearchAsync(new VehicleSaleFilter
        {
            Plaka = F5Ortak.Nz(f.Plaka), AliciCariId = f.AliciCariId, Durum = F5Ortak.EnumAdi<SaleStatus>(f.Durum, "durum"),
            SatisiVerildi = f.SatisiVerildi, Ofis = F5Ortak.Nz(f.Ofis), Bas = bas, Bit = bit,
        }, ct);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var vehicles = await VehicleBranchesAsync(db, rows.Select(s => s.VehicleId), ct);
        var visible = rows.Where(s => InScope(user, vehicles.GetValueOrDefault(s.VehicleId))).ToList();
        var plates = await F5Ortak.PlakalarAsync(dbf, visible.Select(s => s.VehicleId), ct);
        var names = await F5Ortak.CarilerAsync(dbf, visible.Select(s => s.AliciCariId), ct);
        var list = visible.Select(s => new VehicleSaleRow(
            s.Id, s.No, s.Tarih, s.VehicleId, F5Ortak.Plaka(plates, s.VehicleId), s.AliciCariId,
            F5Ortak.CariAdi(names, s.AliciCariId), s.SatisNet, s.KdvOrani, s.KdvTutar, s.GenelToplam, s.Currency, s.Kur,
            s.Durum.ToString(), s.NoterNo, s.NoterSatisTarihi, s.IhaleTarihi, s.IhaleFirmasi, s.SatisKm, s.SatisKanali,
            s.SatisiVerildi, s.Aciklama)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, Sort, sayfa, boyut, sirala));
    }

    private static async Task<Ok<DocumentResult>> Create(
        VehicleSaleCreateRequest req, VehicleSaleService sales, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (req.AracId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.", "aracId");
        if (req.AliciCariId == Guid.Empty) throw new ValidationException("Alıcı (cari) seçilmelidir.", "aliciCariId");
        Amount(req.SatisNet, "satisNet");
        VatRate(req.KdvOrani, "kdvOrani");
        var currency = Currency(req.Doviz);
        Rate(req.Kur);
        BaseLimit(req.SatisNet * (1m + req.KdvOrani), req.Kur, "satisNet");
        AmountLimit(req.HedefFiyat, "hedefFiyat");
        if (req.SatisKm is < 0) throw new ValidationException("Satış KM negatif olamaz.", "satisKm");
        if (req.IlanKm is < 0) throw new ValidationException("İlan KM negatif olamaz.", "ilanKm");
        var listCurrency = string.IsNullOrWhiteSpace(req.ListeDoviz) ? null : Currency(req.ListeDoviz, "listeDoviz");
        Text(req.NoterNo, 64, "noterNo");
        Text(req.Aciklama, 512, "aciklama");
        Text(req.IhaleFirmasi, 128, "ihaleFirmasi");
        Text(req.SatisKanali, 128, "satisKanali");
        Text(req.Devir, 128, "devir");
        Text(req.SatisNoktasi, 128, "satisNoktasi");
        Text(req.UygulananKampanya, 128, "uygulananKampanya");
        Text(req.IhaleSayisi, 64, "ihaleSayisi");
        Text(req.YevmiyeNumarasi, 64, "yevmiyeNumarasi");
        Text(req.Aciklama2, 512, "aciklama2");
        WithField("tarih", () => DatePolicy.MoneyDate(req.Tarih, "Satış"));

        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            RequireInScope(user, (await RequireVehicleAsync(db, req.AracId, "aracId", ct))!.Value);
            await RequireCustomerAsync(db, req.AliciCariId, "aliciCariId", ct);
        }

        var id = await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = req.AracId, AliciCariId = req.AliciCariId, Tarih = F5Ortak.Utc(req.Tarih),
            NoterNo = Trimmed(req.NoterNo), SatisNet = req.SatisNet, KdvOrani = req.KdvOrani, Doviz = currency,
            Kur = req.Kur, Aciklama = Trimmed(req.Aciklama), HedefFiyat = req.HedefFiyat, SatisKm = req.SatisKm,
            SatisKanali = Trimmed(req.SatisKanali), Devir = Trimmed(req.Devir), IhaleTarihi = F5Ortak.Utc(req.IhaleTarihi),
            IhaleFirmasi = Trimmed(req.IhaleFirmasi), NoterSatisTarihi = F5Ortak.Utc(req.NoterSatisTarihi),
            KirayaVerme = req.KirayaVerme, IlanKm = req.IlanKm, ListeDoviz = listCurrency,
            SatisNoktasi = Trimmed(req.SatisNoktasi), UygulananKampanya = Trimmed(req.UygulananKampanya),
            IhaleSayisi = Trimmed(req.IhaleSayisi), SatisiVerildi = req.SatisiVerildi,
            YevmiyeNumarasi = Trimmed(req.YevmiyeNumarasi), Aciklama2 = Trimmed(req.Aciklama2),
        }, ct);
        return TypedResults.Ok(new DocumentResult(id, (await sales.GetAsync(id, ct))?.No ?? ""));
    }
}
