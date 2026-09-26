using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/arac-siparisleri/*</c> (F6.1b) — araç sipariş/tedarik (<see cref="VehicleOrderService"/>). DEFTERE YAZMAZ.
/// <para><b>İzin:</b> okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; yazma OperationsWrite (Blazor grubu; iptal
/// dahil). Siparişin aracı henüz filoda yok → şube kapsamı yok (Blazor ile aynı; kiracı geneli).</para>
/// <para><b>Çift gönderim:</b> oluşturma <c>Idempotency-Key</c> ister (Id = anahtar). Durum geçişleri KİLİT ALTINDA;
/// İptal terminal, aynı duruma ikinci geçiş no-op. PUT tam değiştirme: zorunlu <c>surum</c>, bayat → 409 <c>cakisma</c>.</para>
/// </summary>
public static class VehicleOrderApi
{
    private const string Root = UiApiExtensions.V1 + "/arac-siparisleri";
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static RouteGroupBuilder MapVehicleOrderApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/arac-siparisleri").WithTags("Araç Sipariş");
        g.MapGet("", GetList).MapFields(F5Shared.SortRules)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", Detail)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", Create).MapFields(Rules).RequirePermission(Permission.OperationsWrite)
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPut("/{id:guid}", Update).MapFields(Rules).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/onayla", (Guid id, HttpContext h, VehicleOrderService s, IDbContextFactory<AppDbContext> d, CancellationToken ct)
            => Status(id, OrderStatus.Onaylandi, h, s, d, ct)).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/teslim-al", (Guid id, HttpContext h, VehicleOrderService s, IDbContextFactory<AppDbContext> d, CancellationToken ct)
            => Status(id, OrderStatus.TeslimAlindi, h, s, d, ct)).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/iptal", (Guid id, HttpContext h, VehicleOrderService s, IDbContextFactory<AppDbContext> d, CancellationToken ct)
            => Status(id, OrderStatus.Iptal, h, s, d, ct)).RequirePermission(Permission.OperationsWrite);
        return g;
    }

    private static readonly (string, string)[] Rules =
    [
        ("Tedarikçi", "tedarikci"), ("Adet", "adet"), ("Birim fiyat", "birimFiyat"), ("Kur", "kur"),
        ("Piyasa fiyatı", "piyasaFiyat"), ("Ops fiyatı", "opsFiyat"), ("Filo fiyatı", "filoFiyat"),
        ("Dosya no", "dosyaNo"), ("Satış temsilcisi", "satisTemsilci"), ("Özel temsilci", "ozelTemsilci"),
        ("Versiyon", "versiyon"), ("Opsiyon", "opsiyon"), ("Renk", "renk"), ("İç renk", "icRenk"),
        ("Kaynak tipi", "kaynakTip"), ("Satış tipi", "satisTipi"), ("TSB kayıt no", "tsbKayitNo"), ("Marka", "marka"),
        ("Tip", "tip"), ("Grup", "grup"), ("Açıklama", "aciklama"),
    ];

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Sipariş bulunamadı.");

    private static readonly SortFieldMap<AracSiparisSatiri> Map = SortFieldMap<AracSiparisSatiri>
        .Create(s => s.Id)
        .Alan("no", s => s.No).Alan("tedarikci", s => s.Tedarikci).Alan("siparisTarihi", s => s.SiparisTarihi)
        .Alan("beklenenTeslim", s => s.BeklenenTeslim).Alan("toplam", s => s.Toplam).Alan("durum", s => s.Durum);

    private static async Task<Ok<Sayfa<AracSiparisSatiri>>> GetList(
        HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf, Guid? cariId, string? ara,
        string? arac, string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var write = AuthExtensions.HasPermission(http.User, Permission.OperationsWrite);
        var (min, max) = F5Shared.DayRange(bas, bit);
        var list = await svc.SearchAsync(new AracSiparisFilter
        {
            CariId = cariId, Ara = F5Shared.Nz(ara), Arac = F5Shared.Nz(arac), DosyaNo = F5Shared.Nz(dosyaNo),
            Durum = F5Shared.EnumAdi<OrderStatus>(durum, "durum"), Bas = min, Bit = max,
        }, ct);
        var customers = await F5Shared.CustomersAsync(dbf,
            list.Where(s => s.TedarikciCariId is not null).Select(s => s.TedarikciCariId!.Value), ct);
        var rows = list.Select(s => new AracSiparisSatiri(s.Id, s.No, s.Durum.ToString(), s.Tedarikci,
            s.TedarikciCariId is { } c ? F5Shared.CustomerName(customers, c) : null, s.SiparisTarihi, s.BeklenenTeslim, s.DosyaNo,
            s.Marka, s.Tip, s.Grup, s.Adet, s.BirimFiyat, s.Adet * s.BirimFiyat, s.Currency, s.KrediId, s.Versiyon,
            s.Renk, s.IcRenk, s.KaynakTip, s.SatisTipi, s.PiyasaFiyat, s.OpsFiyat, s.FiloFiyat, s.ImzaTarih,
            s.TsbKayitNo, Permissions(s.Durum, write))).ToList();
        return TypedResults.Ok(F5Shared.Paginate(rows, Map, sayfa, boyut, sirala));
    }

    /// <summary>Durum bayrakları servisin TEK geçiş tablosundan (adversarial M1: ayrı kopya teslim sonrası "onayla"yı
    /// açık bırakmıştı). Liste satırı (F6.2b) ve detay AYNI kuralı kullanır.</summary>
    private static AracSiparisYetkileri Permissions(OrderStatus status, bool write) => new(
        write && status != OrderStatus.Iptal,
        write && VehicleOrderService.IsTransitionAllowed(status, OrderStatus.Onaylandi),
        write && VehicleOrderService.IsTransitionAllowed(status, OrderStatus.TeslimAlindi),
        write && VehicleOrderService.IsTransitionAllowed(status, OrderStatus.Iptal));

    private static async Task<AracSiparisDto?> DtoAsync(Guid id, HttpContext http, VehicleOrderService svc,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var version = await svc.VersionAsync(id, ct); // alanlardan ÖNCE
        var s = await svc.GetAsync(id, ct);
        if (s is null) return null;
        var account = s.TedarikciCariId is { } c ? F5Shared.CustomerName(await F5Shared.CustomersAsync(dbf, [c], ct), c) : null;
        var write = AuthExtensions.HasPermission(http.User, Permission.OperationsWrite);
        return AracSiparisDto.From(s, version, account, Permissions(s.Durum, write));
    }

    private static async Task<Results<Ok<AracSiparisDto>, ProblemHttpResult>> Detail(
        Guid id, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DtoAsync(id, http, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();

    /// <summary>Uç sınırları + varlık (tedarikçi cari, kredi bu kiracıda) + TRY'de kur = 1.</summary>
    private static async Task<AracSiparisInput> InputAsync(AracSiparisIstegi i, IDbContextFactory<AppDbContext> dbf,
        ExchangeRateResolver exchangeRateResolver, CancellationToken ct)
    {
        VehicleFinanceShared.Amount(i.BirimFiyat, "birimFiyat", zeroFree: true);
        VehicleFinanceShared.InfoAmount(i.PiyasaFiyat, "piyasaFiyat");
        VehicleFinanceShared.InfoAmount(i.OpsFiyat, "opsFiyat");
        VehicleFinanceShared.InfoAmount(i.FiloFiyat, "filoFiyat");
        var currency = VehicleFinanceShared.Currency(i.Doviz);
        VehicleFinanceShared.Setup(i.Kur, currency);
        decimal exchangeRate;
        try { exchangeRate = await exchangeRateResolver.ResolveAsync(currency, i.Kur, F5Shared.Utc(i.SiparisTarihi), ct); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, "kur"); }
        await VehicleFinanceShared.CustomerExistsAsync(dbf, i.TedarikciCariId, "tedarikciCariId", required: false, ct);
        if (i.KrediId is { } k && k != Guid.Empty)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            if (!await db.AracKredileri.AsNoTracking().AnyAsync(x => x.Id == k, ct))
                throw new ValidationException("Araç kredisi bulunamadı.", "krediId");
        }
        return VehicleOrderMapping.Input(i, currency, exchangeRate);
    }

    public const string AlreadySaved = "Bu sipariş zaten kaydedildi (No {0}, {1} {2}); yeni sipariş yazılmadı.";
    public const string KeyMismatch =
        "Bu işlem anahtarıyla başka içerikte bir sipariş kaydedilmiş (No {0}, {1} {2}); girdiğiniz sipariş YAZILMADI.";

    private static DuplicateOperationException Existing(AracSiparis m, AracSiparisIstegi i)
    {
        var currency = VehicleFinanceShared.Currency(i.Doviz); // yazımla AYNI normalizasyon (L1: birebir tekrar ayniIcerik=true)
        var same = string.Equals(m.Tedarikci, i.Tedarikci?.Trim(), StringComparison.Ordinal) && m.Adet == (i.Adet ?? 1)
                   && m.BirimFiyat == i.BirimFiyat && m.Currency == currency;
        var total = m.Adet * m.BirimFiyat;
        return new DuplicateOperationException(
            string.Format(Tr, same ? AlreadySaved : KeyMismatch, m.No, total.ToString("N2", Tr), m.Currency),
            new MevcutIslem(m.Id, m.No, total, m.Currency, same));
    }

    private static async Task<Created<AracSiparisOlusturYaniti>> Create(
        AracSiparisIstegi i, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf,
        ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        if (await svc.GetAsync(key, ct) is { } m) throw Existing(m, i); // (1) ÖNCE mevcut kayıt
        var input = await InputAsync(i, dbf, kurCozucu, ct);
        input.IslemAnahtari = key;
        try { await svc.CreateAsync(input, ct); }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            if (await svc.GetAsync(key, ct) is { } y) throw Existing(y, i);
            throw;
        }
        return TypedResults.Created($"{Root}/{key}",
            new AracSiparisOlusturYaniti(key, (await svc.GetAsync(key, ct))?.No ?? ""));
    }

    private static async Task<Results<Ok<AracSiparisDto>, ProblemHttpResult>> Update(
        Guid id, AracSiparisIstegi i, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf,
        ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return NotFoundProblem();
        var version = VehicleFinanceShared.Version(i.Surum);
        var input = await InputAsync(i, dbf, kurCozucu, ct);
        if (!await svc.UpdateVersionedAsync(id, input, version, ct)) return NotFoundProblem();
        return await DtoAsync(id, http, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<AracSiparisDto>, ProblemHttpResult>> Status(
        Guid id, OrderStatus status, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (!await svc.ChangeStatusAsync(id, status, ct)) return NotFoundProblem();
        return await DtoAsync(id, http, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }
}
