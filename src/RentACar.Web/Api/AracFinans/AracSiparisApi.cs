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
public static class AracSiparisApi
{
    private const string Kok = UiApiExtensions.V1 + "/arac-siparisleri";
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static RouteGroupBuilder MapAracSiparisApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/arac-siparisleri").WithTags("Araç Sipariş");
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", Detay)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", Olustur).AlanlariEsle(Kurallar).RequirePermission(Permission.OperationsWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPut("/{id:guid}", Guncelle).AlanlariEsle(Kurallar).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/onayla", (Guid id, HttpContext h, VehicleOrderService s, IDbContextFactory<AppDbContext> d, CancellationToken ct)
            => Durum(id, OrderStatus.Onaylandi, h, s, d, ct)).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/teslim-al", (Guid id, HttpContext h, VehicleOrderService s, IDbContextFactory<AppDbContext> d, CancellationToken ct)
            => Durum(id, OrderStatus.TeslimAlindi, h, s, d, ct)).RequirePermission(Permission.OperationsWrite);
        g.MapPost("/{id:guid}/iptal", (Guid id, HttpContext h, VehicleOrderService s, IDbContextFactory<AppDbContext> d, CancellationToken ct)
            => Durum(id, OrderStatus.Iptal, h, s, d, ct)).RequirePermission(Permission.OperationsWrite);
        return g;
    }

    private static readonly (string, string)[] Kurallar =
    [
        ("Tedarikçi", "tedarikci"), ("Adet", "adet"), ("Birim fiyat", "birimFiyat"), ("Kur", "kur"),
        ("Piyasa fiyatı", "piyasaFiyat"), ("Ops fiyatı", "opsFiyat"), ("Filo fiyatı", "filoFiyat"),
        ("Dosya no", "dosyaNo"), ("Satış temsilcisi", "satisTemsilci"), ("Özel temsilci", "ozelTemsilci"),
        ("Versiyon", "versiyon"), ("Opsiyon", "opsiyon"), ("Renk", "renk"), ("İç renk", "icRenk"),
        ("Kaynak tipi", "kaynakTip"), ("Satış tipi", "satisTipi"), ("TSB kayıt no", "tsbKayitNo"), ("Marka", "marka"),
        ("Tip", "tip"), ("Grup", "grup"), ("Açıklama", "aciklama"),
    ];

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Sipariş bulunamadı.");

    private static readonly SortFieldMap<AracSiparisSatiri> Harita = SortFieldMap<AracSiparisSatiri>
        .Create(s => s.Id)
        .Alan("no", s => s.No).Alan("tedarikci", s => s.Tedarikci).Alan("siparisTarihi", s => s.SiparisTarihi)
        .Alan("beklenenTeslim", s => s.BeklenenTeslim).Alan("toplam", s => s.Toplam).Alan("durum", s => s.Durum);

    private static async Task<Ok<Sayfa<AracSiparisSatiri>>> Liste(
        HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf, Guid? cariId, string? ara,
        string? arac, string? dosyaNo, string? durum, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var yaz = AuthExtensions.HasPermission(http.User, Permission.OperationsWrite);
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var liste = await svc.SearchAsync(new AracSiparisFilter
        {
            CariId = cariId, Ara = F5Ortak.Nz(ara), Arac = F5Ortak.Nz(arac), DosyaNo = F5Ortak.Nz(dosyaNo),
            Durum = F5Ortak.EnumAdi<OrderStatus>(durum, "durum"), Bas = min, Bit = max,
        }, ct);
        var cariler = await F5Ortak.CarilerAsync(dbf,
            liste.Where(s => s.TedarikciCariId is not null).Select(s => s.TedarikciCariId!.Value), ct);
        var satirlar = liste.Select(s => new AracSiparisSatiri(s.Id, s.No, s.Durum.ToString(), s.Tedarikci,
            s.TedarikciCariId is { } c ? F5Ortak.CariAdi(cariler, c) : null, s.SiparisTarihi, s.BeklenenTeslim, s.DosyaNo,
            s.Marka, s.Tip, s.Grup, s.Adet, s.BirimFiyat, s.Adet * s.BirimFiyat, s.Currency, s.KrediId, s.Versiyon,
            s.Renk, s.IcRenk, s.KaynakTip, s.SatisTipi, s.PiyasaFiyat, s.OpsFiyat, s.FiloFiyat, s.ImzaTarih,
            s.TsbKayitNo, Yetkiler(s.Durum, yaz))).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(satirlar, Harita, sayfa, boyut, sirala));
    }

    /// <summary>Durum bayrakları servisin TEK geçiş tablosundan (adversarial M1: ayrı kopya teslim sonrası "onayla"yı
    /// açık bırakmıştı). Liste satırı (F6.2b) ve detay AYNI kuralı kullanır.</summary>
    private static AracSiparisYetkileri Yetkiler(OrderStatus durum, bool yaz) => new(
        yaz && durum != OrderStatus.Iptal,
        yaz && VehicleOrderService.IsTransitionAllowed(durum, OrderStatus.Onaylandi),
        yaz && VehicleOrderService.IsTransitionAllowed(durum, OrderStatus.TeslimAlindi),
        yaz && VehicleOrderService.IsTransitionAllowed(durum, OrderStatus.Iptal));

    private static async Task<AracSiparisDto?> DtoAsync(Guid id, HttpContext http, VehicleOrderService svc,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var surum = await svc.VersionAsync(id, ct); // alanlardan ÖNCE
        var s = await svc.GetAsync(id, ct);
        if (s is null) return null;
        var cari = s.TedarikciCariId is { } c ? F5Ortak.CariAdi(await F5Ortak.CarilerAsync(dbf, [c], ct), c) : null;
        var yaz = AuthExtensions.HasPermission(http.User, Permission.OperationsWrite);
        return AracSiparisDto.From(s, surum, cari, Yetkiler(s.Durum, yaz));
    }

    private static async Task<Results<Ok<AracSiparisDto>, ProblemHttpResult>> Detay(
        Guid id, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DtoAsync(id, http, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    /// <summary>Uç sınırları + varlık (tedarikçi cari, kredi bu kiracıda) + TRY'de kur = 1.</summary>
    private static async Task<AracSiparisInput> GirdiAsync(AracSiparisIstegi i, IDbContextFactory<AppDbContext> dbf,
        ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        AracFinansOrtak.Tutar(i.BirimFiyat, "birimFiyat", sifirSerbest: true);
        AracFinansOrtak.BilgiTutari(i.PiyasaFiyat, "piyasaFiyat");
        AracFinansOrtak.BilgiTutari(i.OpsFiyat, "opsFiyat");
        AracFinansOrtak.BilgiTutari(i.FiloFiyat, "filoFiyat");
        var doviz = AracFinansOrtak.Doviz(i.Doviz);
        AracFinansOrtak.Kur(i.Kur, doviz);
        decimal kur;
        try { kur = await kurCozucu.ResolveAsync(doviz, i.Kur, F5Ortak.Utc(i.SiparisTarihi), ct); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, "kur"); }
        await AracFinansOrtak.CariVarAsync(dbf, i.TedarikciCariId, "tedarikciCariId", zorunlu: false, ct);
        if (i.KrediId is { } k && k != Guid.Empty)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            if (!await db.AracKredileri.AsNoTracking().AnyAsync(x => x.Id == k, ct))
                throw new ValidationException("Araç kredisi bulunamadı.", "krediId");
        }
        return AracSiparisEsleme.Girdi(i, doviz, kur);
    }

    public const string ZatenKaydedildi = "Bu sipariş zaten kaydedildi (No {0}, {1} {2}); yeni sipariş yazılmadı.";
    public const string AnahtarFarkli =
        "Bu işlem anahtarıyla başka içerikte bir sipariş kaydedilmiş (No {0}, {1} {2}); girdiğiniz sipariş YAZILMADI.";

    private static DuplicateOperationException Mevcut(AracSiparis m, AracSiparisIstegi i)
    {
        var doviz = AracFinansOrtak.Doviz(i.Doviz); // yazımla AYNI normalizasyon (L1: birebir tekrar ayniIcerik=true)
        var ayni = string.Equals(m.Tedarikci, i.Tedarikci?.Trim(), StringComparison.Ordinal) && m.Adet == (i.Adet ?? 1)
                   && m.BirimFiyat == i.BirimFiyat && m.Currency == doviz;
        var toplam = m.Adet * m.BirimFiyat;
        return new DuplicateOperationException(
            string.Format(Tr, ayni ? ZatenKaydedildi : AnahtarFarkli, m.No, toplam.ToString("N2", Tr), m.Currency),
            new MevcutIslem(m.Id, m.No, toplam, m.Currency, ayni));
    }

    private static async Task<Created<AracSiparisOlusturYaniti>> Olustur(
        AracSiparisIstegi i, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf,
        ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        if (await svc.GetAsync(anahtar, ct) is { } m) throw Mevcut(m, i); // (1) ÖNCE mevcut kayıt
        var girdi = await GirdiAsync(i, dbf, kurCozucu, ct);
        girdi.IslemAnahtari = anahtar;
        try { await svc.CreateAsync(girdi, ct); }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            if (await svc.GetAsync(anahtar, ct) is { } y) throw Mevcut(y, i);
            throw;
        }
        return TypedResults.Created($"{Kok}/{anahtar}",
            new AracSiparisOlusturYaniti(anahtar, (await svc.GetAsync(anahtar, ct))?.No ?? ""));
    }

    private static async Task<Results<Ok<AracSiparisDto>, ProblemHttpResult>> Guncelle(
        Guid id, AracSiparisIstegi i, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf,
        ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return Bulunamadi();
        var surum = AracFinansOrtak.Surum(i.Surum);
        var girdi = await GirdiAsync(i, dbf, kurCozucu, ct);
        if (!await svc.UpdateVersionedAsync(id, girdi, surum, ct)) return Bulunamadi();
        return await DtoAsync(id, http, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<AracSiparisDto>, ProblemHttpResult>> Durum(
        Guid id, OrderStatus durum, HttpContext http, VehicleOrderService svc, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (!await svc.ChangeStatusAsync(id, durum, ct)) return Bulunamadi();
        return await DtoAsync(id, http, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }
}
