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
/// makinesiyle yapısal korunur (kabul ikinci kez → 400, ikinci rezervasyon AÇILMAZ).</para>
/// </summary>
public static class TeklifApi
{
    private const string Kok = UiApiExtensions.V1 + "/teklifler";

    public static RouteGroupBuilder MapTeklifApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/teklifler")
            .WithTags("Teklif")
            .RequirePermission(Permission.OperationsWrite);

        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/{id:guid}", Detay);
        g.MapPost("", Olustur).AlanlariEsle(YazmaKurallari);
        g.MapPost("/{id:guid}/gonder", Gonder);
        g.MapPost("/{id:guid}/reddet", Reddet);
        g.MapPost("/{id:guid}/kabul", Kabul);
        return g;
    }

    private static readonly SiralamaHaritasi<TeklifListeSatiri> Harita = SiralamaHaritasi<TeklifListeSatiri>
        .Olustur(t => t.Id)
        .Alan("no", t => t.No)
        .Alan("musteri", t => t.MusteriAd)
        .Alan("plaka", t => t.Plaka)
        .Alan("basTar", t => t.BasTar)
        .Alan("gun", t => t.Gun)
        .Alan("tutar", t => t.Tutar)
        .Alan("gecerlilikTarihi", t => t.GecerlilikTarihi)
        .Alan("durum", t => t.Durum);

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Teklif bulunamadı.");

    /// <summary>Teklif listesi (Blazor QuotationList: süzgeç yok; şube kapsamı serviste). İsteğe bağlı <c>durum</c>.</summary>
    private static async Task<Ok<Sayfa<TeklifListeSatiri>>> Liste(
        QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, string? durum, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var d = F5Ortak.EnumAdi<QuotationStatus>(durum, "durum");
        var liste = (await teklifler.ListAsync(ct)).Where(t => d is null || t.Durum == d).ToList();
        var cariler = await F5Ortak.CarilerAsync(dbf, liste.Select(t => t.MusteriId), ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, liste.Select(t => t.VehicleId), ct);
        var satirlar = liste.Select(t => new TeklifListeSatiri(
            t.Id, t.No, t.MusteriId, F5Ortak.CariAdi(cariler, t.MusteriId), t.VehicleId, F5Ortak.Plaka(plakalar, t.VehicleId),
            t.BasTar, t.BitTar, t.Gun, t.Tutar, t.GecerlilikTarihi, t.Durum.ToString(), t.ReservationId)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(satirlar, Harita, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<TeklifDetayYaniti>, ProblemHttpResult>> Detay(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DetayYanitiAsync(id, teklifler, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();

    private static async Task<TeklifDetayYaniti?> DetayYanitiAsync(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var t = await teklifler.GetAsync(id, ct); // kapsam dışı → 403
        if (t is null) return null;
        var cariler = await F5Ortak.CarilerAsync(dbf, [t.MusteriId], ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, [t.VehicleId], ct);
        var acik = t.Durum is QuotationStatus.Taslak or QuotationStatus.Gonderildi;
        return new TeklifDetayYaniti(
            TeklifDto.From(t), F5Ortak.CariAdi(cariler, t.MusteriId), F5Ortak.Plaka(plakalar, t.VehicleId),
            new TeklifYetkileri(Gonder: t.Durum == QuotationStatus.Taslak, Kabul: acik, Reddet: acik));
    }

    private static readonly (string, string)[] YazmaKurallari =
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

    private static async Task<Created<TeklifOlusturYaniti>> Olustur(
        TeklifIstegi istek, QuotationService teklifler, ICustomerRepository musteriler, IVehicleRepository araclar,
        ILocationRepository lokasyonlar, ICurrentUser kullanici, CancellationToken ct)
    {
        Sinirlar.Tutar(istek.GunlukUcret, "gunlukUcret", "Günlük ücret");
        Sinirlar.Tutar(istek.FazlaKmUcret, "fazlaKmUcret", "Fazla km ücreti");
        Sinirlar.Tutar(istek.YakitBirimUcret, "yakitBirimUcret", "Yakıt birim ücreti");
        if (istek.KmLimit is < 0 or > 10_000_000)
            throw new ValidationException("KM limiti 0 ile 10.000.000 arasında olmalıdır.", "kmLimit");
        Sinirlar.Metin(istek.CikisOfisi, 64, "cikisOfisi", "Çıkış ofisi");
        Sinirlar.Metin(istek.DonusOfisi, 64, "donusOfisi", "Dönüş ofisi");
        Sinirlar.Metin(istek.Aciklama, 1024, "aciklama", "Açıklama");
        Sinirlar.Metin(istek.FiyatTuru, 64, "fiyatTuru", "Fiyat türü");
        TarihPolitikasi.KiraBitis(istek.BasTar, istek.BitTar); // teklif → rezervasyon → kira zinciri
        await F5Ortak.CikisOfisiKapsamiAsync(lokasyonlar, kullanici, istek.CikisOfisi, ct);
        await RezervasyonApi.VarlikAsync(musteriler, araclar, istek.MusteriId, istek.VehicleId, ct);
        var id = await teklifler.CreateAsync(new QuotationInput
        {
            MusteriId = istek.MusteriId, VehicleId = istek.VehicleId,
            BasTar = F5Ortak.Utc(istek.BasTar), BitTar = F5Ortak.Utc(istek.BitTar),
            GunlukUcret = istek.GunlukUcret ?? 0m, FiyatTuru = F5Ortak.Nz(istek.FiyatTuru),
            CikisOfisi = F5Ortak.Nz(istek.CikisOfisi), DonusOfisi = F5Ortak.Nz(istek.DonusOfisi),
            KmLimit = istek.KmLimit ?? 0, FazlaKmUcret = istek.FazlaKmUcret ?? 0m, YakitBirimUcret = istek.YakitBirimUcret ?? 0m,
            GecerlilikTarihi = F5Ortak.Utc(istek.GecerlilikTarihi), Aciklama = F5Ortak.Nz(istek.Aciklama),
        }, ct);
        var no = (await teklifler.GetAsync(id, ct))?.No ?? "";
        return TypedResults.Created($"{Kok}/{id}", new TeklifOlusturYaniti(id, no));
    }

    private static async Task<Results<Ok<TeklifDetayYaniti>, ProblemHttpResult>> Gonder(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await teklifler.GetAsync(id, ct) is null) return Bulunamadi();
        if (!await teklifler.SendAsync(id, ct)) return Bulunamadi();
        return await DetayYanitiAsync(id, teklifler, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();
    }

    private static async Task<Results<Ok<TeklifDetayYaniti>, ProblemHttpResult>> Reddet(
        Guid id, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await teklifler.GetAsync(id, ct) is null) return Bulunamadi();
        if (!await teklifler.RejectAsync(id, ct)) return Bulunamadi();
        return await DetayYanitiAsync(id, teklifler, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();
    }

    /// <summary>Kabul → rezervasyon (teklifin fiyat taahhüdü yeniden fiyatlanmadan taşınır). Yeni rezervasyon kimliği döner.</summary>
    private static async Task<Results<Ok<TeklifKabulYaniti>, ProblemHttpResult>> Kabul(
        Guid id, QuotationService teklifler, ReservationService rezervasyonlar, CancellationToken ct)
    {
        if (await teklifler.GetAsync(id, ct) is null) return Bulunamadi();
        var rezId = await teklifler.AcceptAsync(id, ct);
        var no = (await rezervasyonlar.GetAsync(rezId, ct))?.ReservationNo ?? "";
        return TypedResults.Ok(new TeklifKabulYaniti(rezId, no));
    }
}

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
