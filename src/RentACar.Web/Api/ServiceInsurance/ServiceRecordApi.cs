using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// <c>/api/ui/v1/servisler/*</c> (F9.1) — servis/bakım kaydı: liste, detay, oluştur, bilgi blokları (PUT, sürümlü),
/// durum akışı (Rezerve → Açık → Serviste → Tamamlandı / İptal), kalem ekleme, rücu yansıtma. İş mantığı
/// <see cref="ServiceRecordService"/>'te.
/// <para><b>İzin:</b> okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; yazma OperationsWrite; iptal OperationsDelete;
/// yansıtma FinanceWrite (defter yazar: Borç Cari / Alacak Gelir).</para>
/// <para><b>Kapsam:</b> ARACIN şubesi; tekil uçlarda durumdan ÖNCE 403.</para>
/// <para><b>Para:</b> kalem <c>toplamIscilik</c>'i büyütür (rücu tabanı) → <c>Idempotency-Key</c> ZORUNLU, kalem Id'si
/// anahtardan; ikinci gönderim 409 <c>mukerrer</c> (+ <c>mevcut</c>). Yansıtma yapısal (kayıt başına tek; SourceId =
/// servis) → ikinci gönderim 409 <c>mukerrer</c> + <c>mevcut</c>; tutar kilit altında yeniden doğrulanır.</para>
/// </summary>
internal static partial class ServiceRecordApi
{
    private const string Root = UiApiExtensions.V1 + "/servisler";
    private static readonly Permission[] ReadAny = [Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports];

    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/servisler").WithTags("Servis");
        g.MapGet("/secenekler", () => TypedResults.Ok(new ServiceOptions(Enum.GetNames<ServisTipi>(), Enum.GetNames<ServisDurum>(),
            Enum.GetNames<HasarSorumlu>(), Enum.GetNames<OdemeYontemi>()))).RequireAnyPermission(ReadAny);
        g.MapGet("", List).AlanlariEsle(F5Ortak.SiralamaKurallari).RequireAnyPermission(ReadAny);
        g.MapGet("/sayaclar", Counts).RequireAnyPermission(ReadAny);
        g.MapGet("/{id:guid}", Detail).RequireAnyPermission(ReadAny);
        g.MapPost("", Create).AlanlariEsle(Rules).RequirePermission(Permission.OperationsWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPut("/{id:guid}/bilgi", UpdateInfo).AlanlariEsle(Rules).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/servise-al", Intake).AlanlariEsle(Rules).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/baslat", Start).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/tamamla", Complete).AlanlariEsle(Rules).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        g.MapPost("/{id:guid}/kalemler", AddLine).AlanlariEsle(Rules).RequirePermission(Permission.OperationsWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/{id:guid}/yansit", Reflect).AlanlariEsle(Rules).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
    }

    private static readonly (string, string)[] Rules =
    [
        ("Araç seçilmelidir", "vehicleId"), ("Giriş KM", "girisKm"), ("Çıkış KM", "cikisKm"), ("Kusur oranı", "kusurOrani"),
        ("Değer kaybı", "degerKaybi"), ("Fatura tutarı", "faturaTutar"), ("Fatura KDV", "faturaKdv"), ("Ödeme tutarı", "odeme"),
        ("Ödeme kuru", "odemeKur"), ("Ödeme dövizi", "odemeDoviz"), ("Çıkış yakıt", "cikisYakit"), ("Dönüş yakıt", "donusYakit"),
        ("Kaza tarihi", "kazaTarihi"), ("Fatura tarihi", "faturaTarihi"), ("Ödeme tarihi", "odemeTarihi"),
        ("Plan bitiş", "planBitTarihi"), ("Kalem açıklaması", "aciklama"), ("Kalem birim fiyatı", "birimFiyat"),
        ("Kalem miktarı", "miktar"), ("Kalem indirimi", "indirim"), ("Kalem KDV", "kdvOran"), ("Kalem tutarı", "tutar"),
        ("Yansıtılacak cari", "cariId"),
    ];

    private static readonly SiralamaHaritasi<ServiceRecordRow> Sort = SiralamaHaritasi<ServiceRecordRow>
        .Olustur(x => x.Id).Alan("no", x => x.No).Alan("plaka", x => x.Plaka).Alan("durum", x => x.Durum).Alan("tip", x => x.Tip)
        .Alan("girisTarihi", x => x.GirisTarihi).Alan("toplamIscilik", x => x.ToplamIscilik);

    private static async Task<Ok<Sayfa<ServiceRecordRow>>> List(
        string? durum, string? tip, string? plaka, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut, string? sirala,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var d = F5Ortak.EnumAdi<ServisDurum>(durum, "durum");
        var list = (await RowsAsync(tip, plaka, bas, bit, svc, dbf, user, ct))
            .Where(r => d is null || r.Durum == d.Value.ToString()).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(list, Sort, sayfa, boyut, sirala));
    }

    /// <summary>
    /// Durum sekmelerinin sayaçları (#301; Blazor "Tümü (n) · Rezerve (n)…"): listeyle AYNI süzgeçler ve kapsam, durum
    /// süzgeci HARİÇ (her sekme kendi sayısını gösterir). Tüm durumlar sıfır dahil, enum sırasıyla döner.
    /// </summary>
    private static async Task<Ok<ServiceCounts>> Counts(
        string? tip, string? plaka, DateOnly? bas, DateOnly? bit,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var rows = await RowsAsync(tip, plaka, bas, bit, svc, dbf, user, ct);
        var counts = Enum.GetNames<ServisDurum>().Select(n => new ServiceStatusCount(n, rows.Count(r => r.Durum == n))).ToList();
        return TypedResults.Ok(new ServiceCounts(rows.Count, counts));
    }

    private static async Task<List<ServiceRecordRow>> RowsAsync(
        string? tip, string? plaka, DateOnly? bas, DateOnly? bit,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var t = F5Ortak.EnumAdi<ServisTipi>(tip, "tip");
        S.Text(plaka, 32, "plaka"); // SPA süzgeci en çok 32
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var rows = (await svc.ListAsync(ct)).Where(s => (t is null || s.Tip == t)
            && (min is null || s.GirisTarihi >= min) && (max is null || s.GirisTarihi <= max));
        var visible = await S.VisibleAsync(dbf, user, rows, s => s.VehicleId, ct);
        var plates = await S.PlatesAsync(dbf, visible.Select(s => s.VehicleId), ct);
        return visible.Select(s => ServiceRecordRow.From(s, F5Ortak.Plaka(plates, s.VehicleId)))
            .Where(r => F5Ortak.Nz(plaka) is not { } q || r.Plaka.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Record (with lines) through the vehicle-branch gate — 403 BEFORE any state check; null = 404.</summary>
    private static async Task<ServiceRecord?> ScopedAsync(Guid id, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        var r = await svc.GetAsync(id, ct);
        if (r is null) return null;
        await S.RecordScopeAsync(dbf, user, r.VehicleId, ct);
        return r;
    }

    private static async Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Detail(
        Guid id, HttpContext http, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
        => await DetailAsync(id, http, svc, dbf, user, ct) is { } d ? TypedResults.Ok(d) : NotFound();

    private static ProblemHttpResult NotFound() => S.NotFound("Servis kaydı bulunamadı.");

    private static async Task<ServiceRecordDetail?> DetailAsync(Guid id, HttpContext http, ServiceRecordService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var version = await svc.GetVersionAsync(id, ct); // BEFORE the fields (stale read → older version → 409 later)
        if (await ScopedAsync(id, svc, dbf, user, ct) is not { } r) return null;
        var lines = r.Lines.Select(ServiceLineDto.From).ToList();
        ServiceReflectionDto? reflection = null;
        if (r.Yansitildi)
        {
            var names = await S.CustomersAsync(dbf, [r.YansitilanCariId], ct);
            await using var db = await dbf.CreateDbContextAsync(ct);
            var at = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => e.SourceType == "ServisYansitma" && e.SourceId == r.Id && e.Direction == LedgerDirection.Debit)
                .Select(e => (DateTimeOffset?)e.EntryDateUtc).FirstOrDefaultAsync(ct);
            reflection = new ServiceReflectionDto(r.YansitilanTutar, r.YansitilanCariId, S.CustomerName(names, r.YansitilanCariId), at);
        }
        var ops = AuthExtensions.HasPermission(http.User, Permission.OperationsWrite);
        var closed = r.Durum is ServisDurum.Tamamlandi or ServisDurum.Iptal;
        var reflectable = r.Durum == ServisDurum.Tamamlandi && !r.Yansitildi
                          && (r.HasarSorumlu is HasarSorumlu.Musteri or HasarSorumlu.Sigorta) && r.KusurOrani is > 0m;
        var actions = new ServiceActions(
            ops && r.Durum == ServisDurum.Rezerve, ops && r.Durum == ServisDurum.Acik, ops && r.Durum == ServisDurum.Serviste,
            !closed && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete), ops && !closed,
            reflectable && AuthExtensions.HasPermission(http.User, Permission.FinanceWrite),
            reflectable ? decimal.Round(r.ToplamIscilik * r.KusurOrani!.Value, 2, MidpointRounding.AwayFromZero) : null);
        return new ServiceRecordDetail(ServiceRecordRow.From(r, await S.PlateAsync(dbf, r.VehicleId, ct)), ServiceInfoDto.From(r),
            lines, lines.Sum(l => l.KdvTutar), lines.Sum(l => l.GenelToplam), reflection, actions, version);
    }
}
