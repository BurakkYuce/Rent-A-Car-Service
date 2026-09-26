using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Locations;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary>
/// <c>/api/ui/v1/teklifler/*</c> — teklif (quotation) JSON uçları (F5.1). İş mantığı <see cref="QuotationService"/>'te
/// (fiyat motoru, Taslak→Gönderildi→Kabul/Red durum makinesi). İzin: OperationsWrite (Blazor grubu ve menü kaydı).
/// <para>Kimlikli her uç ÖNCE <see cref="QuotationService.GetAsync"/>'ten geçer (kapsam dışı 403, yok 404) — durum
/// kontrolünden önce. Blazor'da teklif düzenleme yok → PUT yok (parite).</para>
/// <para>Çift gönderim: oluşturma anahtarsız (Blazor ile aynı; SPA düğmeyi kilitler); gönder/reddet/kabul durum
/// makinesiyle yapısal korunur. Kabul teklif satırını kilitler; ikinci kabul (eşzamanlı ya da yanıtı kaybolan tekrar)
/// → 409 <c>cakisma</c>, ikinci rezervasyon AÇILMAZ (ayrıca <c>(TenantId, KaynakTeklifId)</c> kısmi UNIQUE — F5.1 adversarial H1).</para>
/// </summary>
public static class QuotationApi
{
    private const string Root = UiApiExtensions.V1 + "/teklifler";

    public static RouteGroupBuilder MapQuotationApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/teklifler")
            .WithTags("Teklif")
            .RequirePermission(Permission.OperationsWrite);

        g.MapGet("", GetList).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create).MapFields(WriteRules);
        g.MapPost("/{id:guid}/gonder", Gonder);
        g.MapPost("/{id:guid}/reddet", Reject);
        g.MapPost("/{id:guid}/kabul", Accept)
            .Produces<TeklifKabulCakismaProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        return g;
    }

    private static readonly SortFieldMap<TeklifListeSatiri> Map = SortFieldMap<TeklifListeSatiri>
        .Create(t => t.Id)
        .Alan("no", t => t.No)
        .Alan("musteri", t => t.MusteriAd)
        .Alan("plaka", t => t.Plaka)
        .Alan("basTar", t => t.BasTar)
        .Alan("gun", t => t.Gun)
        .Alan("tutar", t => t.Tutar)
        .Alan("gecerlilikTarihi", t => t.GecerlilikTarihi)
        .Alan("durum", t => t.Durum);

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Teklif bulunamadı.");

    /// <summary>Teklif listesi (Blazor QuotationList: süzgeç yok; şube kapsamı serviste). İsteğe bağlı <c>durum</c>.</summary>
    private static async Task<Ok<Sayfa<TeklifListeSatiri>>> GetList(
        QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, string? durum, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var d = F5Shared.EnumAdi<QuotationStatus>(durum, "durum");
        var list = (await teklifler.ListAsync(ct)).Where(t => d is null || t.Durum == d).ToList();
        var customers = await F5Shared.CustomersAsync(dbf, list.Select(t => t.MusteriId), ct);
        var plates = await F5Shared.PlatesAsync(dbf, list.Select(t => t.VehicleId), ct);
        var rows = list.Select(t => new TeklifListeSatiri(
            t.Id, t.No, t.MusteriId, F5Shared.CustomerName(customers, t.MusteriId), t.VehicleId, F5Shared.Plate(plates, t.VehicleId),
            t.BasTar, t.BitTar, t.Gun, t.Tutar, t.GecerlilikTarihi, t.Durum.ToString(), t.ReservationId)).ToList();
        return TypedResults.Ok(F5Shared.Paginate(rows, Map, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<TeklifDetayYaniti>, ProblemHttpResult>> Detail(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DetailResponseAsync(id, teklifler, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();

    private static async Task<TeklifDetayYaniti?> DetailResponseAsync(
        Guid id, QuotationService quotations, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var t = await quotations.GetAsync(id, ct); // kapsam dışı → 403
        if (t is null) return null;
        var customers = await F5Shared.CustomersAsync(dbf, [t.MusteriId], ct);
        var plates = await F5Shared.PlatesAsync(dbf, [t.VehicleId], ct);
        var open = t.Durum is QuotationStatus.Taslak or QuotationStatus.Gonderildi;
        return new TeklifDetayYaniti(
            TeklifDto.From(t), F5Shared.CustomerName(customers, t.MusteriId), F5Shared.Plate(plates, t.VehicleId),
            new TeklifYetkileri(Gonder: t.Durum == QuotationStatus.Taslak, Kabul: open, Reddet: open));
    }

    private static readonly (string, string)[] WriteRules =
    [
        ("Müşteri seçilmelidir", "musteriId"),
        ("Araç seçilmelidir", "vehicleId"),
        ("Bitiş tarihi başlangıçtan sonra", "bitTar"),
        ("Kira süresi en fazla", "bitTar"),
        ("Rezervasyon geçmiş tarihe", "basTar"),
        ("Rezervasyon en fazla 1 yıl", "basTar"),
        ("Günlük ücret negatif", "gunlukUcret"),
        ("Otomatik tarife bulunamadı", "gunlukUcret"),
        ("Geçerlilik tarihi", "gecerlilikTarihi"),
    ];

    private static async Task<Created<TeklifOlusturYaniti>> Create(
        TeklifIstegi istek, QuotationService teklifler, ILocationRepository lokasyonlar, ICurrentUser kullanici,
        CancellationToken ct)
    {
        RentalLimits.Amount(istek.GunlukUcret, "gunlukUcret", "Günlük ücret");
        RentalLimits.Amount(istek.FazlaKmUcret, "fazlaKmUcret", "Fazla km ücreti");
        RentalLimits.Amount(istek.YakitBirimUcret, "yakitBirimUcret", "Yakıt birim ücreti");
        if (istek.KmLimit is < 0 or > 10_000_000)
            throw new ValidationException("KM limiti 0 ile 10.000.000 arasında olmalıdır.", "kmLimit");
        RentalLimits.Text(istek.CikisOfisi, 64, "cikisOfisi", "Çıkış ofisi");
        RentalLimits.Text(istek.DonusOfisi, 64, "donusOfisi", "Dönüş ofisi");
        RentalLimits.Text(istek.Aciklama, 1024, "aciklama", "Açıklama");
        RentalLimits.Text(istek.FiyatTuru, 64, "fiyatTuru", "Fiyat türü");
        DatePolicy.RentalEnd(istek.BasTar, istek.BitTar); // teklif → rezervasyon → kira zinciri
        await F5Shared.PickupOfficeScopeAsync(lokasyonlar, kullanici, istek.CikisOfisi, ct);
        // Müşteri/araç varlık kontrolü QuotationService.CreateAsync girişinde (BookingPartyCheck; tek kural).
        var id = await teklifler.CreateAsync(new QuotationInput
        {
            MusteriId = istek.MusteriId, VehicleId = istek.VehicleId,
            BasTar = F5Shared.Utc(istek.BasTar), BitTar = F5Shared.Utc(istek.BitTar),
            GunlukUcret = istek.GunlukUcret ?? 0m, FiyatTuru = F5Shared.Nz(istek.FiyatTuru),
            CikisOfisi = F5Shared.Nz(istek.CikisOfisi), DonusOfisi = F5Shared.Nz(istek.DonusOfisi),
            KmLimit = istek.KmLimit ?? 0, FazlaKmUcret = istek.FazlaKmUcret ?? 0m, YakitBirimUcret = istek.YakitBirimUcret ?? 0m,
            GecerlilikTarihi = F5Shared.Utc(istek.GecerlilikTarihi), Aciklama = F5Shared.Nz(istek.Aciklama),
        }, ct);
        var no = (await teklifler.GetAsync(id, ct))?.No ?? "";
        return TypedResults.Created($"{Root}/{id}", new TeklifOlusturYaniti(id, no));
    }

    private static async Task<Results<Ok<TeklifDetayYaniti>, ProblemHttpResult>> Gonder(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await teklifler.GetAsync(id, ct) is null) return NotFoundProblem();
        if (!await teklifler.SendAsync(id, ct)) return NotFoundProblem();
        return await DetailResponseAsync(id, teklifler, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();
    }

    private static async Task<Results<Ok<TeklifDetayYaniti>, ProblemHttpResult>> Reject(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await teklifler.GetAsync(id, ct) is null) return NotFoundProblem();
        if (!await teklifler.RejectAsync(id, ct)) return NotFoundProblem();
        return await DetailResponseAsync(id, teklifler, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();
    }

    /// <summary>Kabul → rezervasyon (teklifin fiyat taahhüdü yeniden fiyatlanmadan taşınır). Yeni rezervasyon kimliği döner.</summary>
    private static async Task<Results<Ok<TeklifKabulYaniti>, ProblemHttpResult>> Accept(
        Guid id, QuotationService teklifler, ReservationService rezervasyonlar, CancellationToken ct)
    {
        if (await teklifler.GetAsync(id, ct) is null) return NotFoundProblem();
        Guid resId;
        try
        {
            resId = await teklifler.AcceptAsync(id, ct);
        }
        catch (ConcurrentModificationException ex) when (ex.Message == ConcurrentModificationException.QuotationAcceptMessage)
        {
            // #271 L3: tekrar (yanıtı kaybolan ya da eşzamanlı ikinci kabul) 409 cakisma alır; gövde ZATEN açılmış
            // rezervasyonu söyler ki SPA kullanıcıyı ona götürsün (ikinci rezervasyon açılmaz — yapısal çit aynen).
            // Rezervasyon kapsam dışıysa (ofisi sonradan değişmiş) kimliği de sızdırılmaz: mevcut'suz 409.
            if (await ExistingReservationAsync(id, teklifler, rezervasyonlar, ct) is { } m)
                return UiError.Problem(UiError.ConflictCode, ex.Message, new Dictionary<string, object?>
                {
                    ["rezervasyonId"] = m.Id, ["rezervasyonNo"] = m.No,
                });
            throw;
        }
        var no = (await rezervasyonlar.GetAsync(resId, ct))?.ReservationNo ?? "";
        return TypedResults.Ok(new TeklifKabulYaniti(resId, no));
    }

    private static async Task<(Guid Id, string No)?> ExistingReservationAsync(
        Guid quotationId, QuotationService quotations, ReservationService reservations, CancellationToken ct)
    {
        if ((await quotations.GetAsync(quotationId, ct))?.ReservationId is not { } rid) return null;
        try
        {
            return await reservations.GetAsync(rid, ct) is { } r ? (r.Id, r.ReservationNo) : null;
        }
        catch (NoPermissionException) { return null; }
    }
}

/// <summary>OpenAPI: <c>POST /teklifler/{id}/kabul</c> tekrarında 409 <c>cakisma</c> gövdesi — <c>mevcut</c> zaten
/// açılmış rezervasyon (#271 L3). Yanıt gerçekte <c>UiHata.Problem(kod, detay, mevcut)</c> ile yazılır.</summary>
public sealed record TeklifKabulCakismaProblemi(
    string Type, string Title, int Status, string Detail, string Kod, TeklifKabulMevcut? Mevcut);

public sealed record TeklifKabulMevcut(Guid RezervasyonId, string RezervasyonNo);

/// <summary><c>POST /teklifler</c> — Blazor teklif formu + servis girdisinin (QuotationInput) tamamı.</summary>
public sealed record TeklifIstegi
{
    public required Guid MusteriId { get; init; }
    public required Guid VehicleId { get; init; }
    public required DateTimeOffset BasTar { get; init; }
    public required DateTimeOffset BitTar { get; init; }
    /// <summary>Boş/0 → tarife (fiyat motoru).</summary>
    public decimal? GunlukUcret { get; init; }
    public string? FiyatTuru { get; init; }
    public string? CikisOfisi { get; init; }
    public string? DonusOfisi { get; init; }
    public DateTimeOffset? GecerlilikTarihi { get; init; }
    public string? Aciklama { get; init; }
    public int? KmLimit { get; init; }
    public decimal? FazlaKmUcret { get; init; }
    public decimal? YakitBirimUcret { get; init; }
}

public sealed record TeklifListeSatiri(
    Guid Id, string No, Guid MusteriId, string MusteriAd, Guid VehicleId, string Plaka, DateTimeOffset BasTar,
    DateTimeOffset BitTar, int Gun, decimal Tutar, DateTimeOffset? GecerlilikTarihi, string Durum, Guid? RezervasyonId);

public sealed record TeklifYetkileri(bool Gonder, bool Kabul, bool Reddet);

public sealed record TeklifDetayYaniti(TeklifDto Teklif, string MusteriAd, string Plaka, TeklifYetkileri Yetkiler);

public sealed record TeklifOlusturYaniti(Guid Id, string No);

public sealed record TeklifKabulYaniti(Guid RezervasyonId, string RezervasyonNo);

public sealed record TeklifDto(
    Guid Id, string No, string Durum, Guid MusteriId, Guid VehicleId, DateTimeOffset BasTar, DateTimeOffset BitTar,
    string? CikisOfisi, string? DonusOfisi, int Gun, decimal GunlukUcret, decimal Tutar, int? HediyeGun,
    int? FaturalananGun, decimal? IskontoTutar, decimal? HaftaSonuFark, string? FiyatTuru, decimal? KdvOranSnapshot,
    int KmLimit, decimal FazlaKmUcret, decimal YakitBirimUcret, DateTimeOffset? GecerlilikTarihi, string? Aciklama,
    Guid? RezervasyonId, DateTimeOffset OlusturmaUtc)
{
    public static TeklifDto From(Quotation t) => new(
        t.Id, t.No, t.Durum.ToString(), t.MusteriId, t.VehicleId, t.BasTar, t.BitTar, t.CikisOfisi, t.DonusOfisi,
        t.Gun, t.GunlukUcret, t.Tutar, t.HediyeGun, t.FaturalananGun, t.IskontoTutar, t.HaftaSonuFark, t.FiyatTuru,
        t.KdvOranSnapshot, t.KmLimit, t.FazlaKmUcret, t.YakitBirimUcret, t.GecerlilikTarihi, t.Aciklama,
        t.ReservationId, t.CreatedAtUtc);
}
