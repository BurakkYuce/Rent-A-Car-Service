using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
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
/// <c>/api/ui/v1/arac-kredileri/*</c> (F6.1b) — araç kredisi. İş mantığı <see cref="VehicleLoanService"/>'te.
/// <para><b>İzin:</b> okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (servis <c>SearchAsync</c> ile aynı); oluşturma
/// OperationsWrite; taksit ödeme FinanceWrite (defter yazar — Blazor adversarial 1.3 M2); iptal OperationsDelete.</para>
/// <para><b>Kapsam:</b> kredi ARACIN şubesinden geçer (alt kayıt kuralı). Araçsız kredi yalnız şube kısıtsız kullanıcıya
/// görünür. Tekil uçlarda kapsam durumdan ÖNCE (403).</para>
/// <para><b>Para (DEVIR §5):</b> taksit ödeme = gerçek gider + dengeli defter (Borç Gider[araç] / Alacak Kasa-Banka),
/// <c>Idempotency-Key</c> zorunlu. Sıra: (1) bu anahtarla yazılmış gider VAR mı → 409 <c>mukerrer</c> + <c>mevcut</c>
/// (kaybolan yanıttan sonraki tekrar ikinci taksidi ödemez); (2) SONRA bayatlık: istemcinin ödemek istediği
/// <c>sira</c> kilit altında <c>OdenenTaksit + 1</c> ile karşılaştırılır → 409 <c>cakisma</c>. Kur servisteki
/// <see cref="ExchangeRateResolver"/>'den (TRY = 1). Oluşturma da anahtarlı: kredi Id'si = anahtar (ikinci kredi PK'ye çarpar).</para>
/// </summary>
public static partial class AracKrediApi
{
    private const string Kok = UiApiExtensions.V1 + "/arac-kredileri";

    /// <summary>Kredi tutarı üst sınırı: faiz (≤ %1000) × 30 yıl ile toplam geri ödeme <c>numeric(19,4)</c>'e sığar.</summary>
    public const decimal EnFazlaKrediTutari = 1_000_000_000_000m;
    public const decimal EnFazlaFaizOrani = 10m;

    public static RouteGroupBuilder MapAracKrediApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/arac-kredileri").WithTags("Araç Kredisi");
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/ozet", Ozet)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", Detay)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", Olustur).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/taksit-ode", TaksitOde).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/{id:guid}/iptal", Iptal).RequirePermission(Permission.OperationsDelete);
        g.MapPost("/toplu-iptal", TopluIptal).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Kredi bulunamadı.");

    private static readonly SortFieldMap<AracKrediListeSatiri> Harita = SortFieldMap<AracKrediListeSatiri>
        .Create(k => k.Id)
        .Alan("no", k => k.No).Alan("bankaAdi", k => k.BankaAdi).Alan("plaka", k => k.Plaka)
        .Alan("cari", k => k.CariAd).Alan("krediTutari", k => k.KrediTutari).Alan("baslangicTarihi", k => k.BaslangicTarihi)
        .Alan("kalanBakiye", k => k.KalanBakiye).Alan("durum", k => k.Durum);

    /// <summary>Filtreli + kapsamlı kredi kümesi (liste ve özet kartları AYNI kümeden).</summary>
    private static async Task<List<AracKredi>> KumeAsync(VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, Guid? cariId, string? plaka, string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit,
        CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var liste = await svc.SearchAsync(new AracKrediFilter
        {
            CariId = cariId, Plaka = F5Ortak.Nz(plaka), DosyaNo = F5Ortak.Nz(dosyaNo),
            Durum = F5Ortak.EnumAdi<LoanStatus>(durum, "durum"), Bas = min, Bit = max,
        }, ct);
        var f = BranchScope.EffectiveFilter(kullanici);
        if (f.Unrestricted) return liste.ToList();
        var subeler = await AracFinansOrtak.AracSubeleriAsync(dbf, liste.Where(k => k.VehicleId is not null)
            .Select(k => k.VehicleId!.Value), ct);
        return liste.Where(k => AracFinansOrtak.Gorunur(f, k.VehicleId, subeler)).ToList();
    }

    private static async Task<Ok<Sayfa<AracKrediListeSatiri>>> Liste(
        VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId, string? plaka,
        string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut, string? sirala,
        CancellationToken ct)
    {
        var liste = await KumeAsync(svc, dbf, kullanici, cariId, plaka, dosyaNo, durum, bas, bit, ct);
        var cariler = await F5Ortak.CarilerAsync(dbf, liste.Where(k => k.CariId is not null).Select(k => k.CariId!.Value), ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, liste.Where(k => k.VehicleId is not null).Select(k => k.VehicleId!.Value), ct);
        var satirlar = liste.Select(k =>
        {
            var oz = VehicleLoanService.Calculate(k);
            return new AracKrediListeSatiri(k.Id, k.No, k.BankaAdi, k.VehicleId,
                k.VehicleId is { } v ? F5Ortak.Plaka(plakalar, v) : null, k.CariId,
                k.CariId is { } c ? F5Ortak.CariAdi(cariler, c) : null, k.DosyaNo, k.KrediTutari, k.FaizOran,
                k.TaksitSayisi, k.OdenenTaksit, k.BaslangicTarihi, k.Currency, k.Durum.ToString(),
                oz.ToplamGeriOdeme, oz.AylikTaksit, oz.KalanBakiye, oz.SonVadeGunu);
        }).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(satirlar, Harita, sayfa, boyut, sirala));
    }

    /// <summary>Liste üstü 5 özet kart (filtreli küme; iptal hariç). Salt gösterge — deftere yazmaz.</summary>
    private static async Task<Ok<AracKrediPano>> Ozet(
        VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId, string? plaka,
        string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit, CancellationToken ct)
        => TypedResults.Ok(VehicleLoanService.Dashboard(
            await KumeAsync(svc, dbf, kullanici, cariId, plaka, dosyaNo, durum, bas, bit, ct), DateTimeOffset.UtcNow));

    private static async Task<Results<Ok<AracKrediDetayYaniti>, ProblemHttpResult>> Detay(
        Guid id, HttpContext http, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
        => await DetayAsync(id, http, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    /// <summary>Kredi (kapsam kapısından geçmiş) ya da null.</summary>
    private static async Task<AracKredi?> KapsamliAsync(Guid id, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, CancellationToken ct)
    {
        var k = await svc.GetAsync(id, ct);
        if (k is null) return null;
        await AracFinansOrtak.KayitKapsamiAsync(dbf, kullanici, k.VehicleId, ct); // durumdan ÖNCE (403)
        return k;
    }

    private static async Task<AracKrediDetayYaniti?> DetayAsync(Guid id, HttpContext http, VehicleLoanService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        var k = await KapsamliAsync(id, svc, dbf, kullanici, ct);
        if (k is null) return null;
        var plaka = k.VehicleId is { } v ? F5Ortak.Plaka(await F5Ortak.PlakalarAsync(dbf, [v], ct), v) : null;
        var cari = k.CariId is { } c ? F5Ortak.CariAdi(await F5Ortak.CarilerAsync(dbf, [c], ct), c) : null;
        var aktif = k.Durum == LoanStatus.Aktif;
        var y = new AracKrediYetkileri(
            aktif && AuthExtensions.HasPermission(http.User, Permission.FinanceWrite),
            aktif && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete));
        return AracKrediDetayYaniti.From(k, plaka, cari, y);
    }
}
