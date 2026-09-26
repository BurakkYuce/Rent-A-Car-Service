using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Locations;
using RentACar.Application.Pricing;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary>
/// <c>/api/ui/v1/rezervasyonlar/*</c> — rezervasyon JSON uçları (F5.1). <b>İş mantığı YOK:</b> her uç
/// <see cref="ReservationService"/>'i çağırır (fiyat motoru, kaynak kural matrisi, durum makinesi orada). Blazor
/// uçları (<c>BookingEndpoints</c> <c>/rezervasyonlar/*</c>) F5 kesişine kadar DEĞİŞMEDEN yaşar.
///
/// <para><b>İzin haritası</b> (Blazor ile aynı): hepsi OperationsWrite (menü kaydı + Blazor grubu); iptal ayrıca
/// OperationsDelete (Blazor <c>/rezervasyonlar/cancel</c> dar izni).</para>
///
/// <para><b>Şube kapsamı:</b> kimlikli her uç ÖNCE <see cref="ReservationService.GetAsync"/>'ten geçer (kapsam dışı →
/// 403 <c>yetki_yok</c>; yok/başka kiracı → 404) — durum kontrolünden ÖNCE (başka şubenin kaydının durumu hata
/// metniyle sızmaz). Oluştur/güncellemede çıkış ofisi GİRİŞTE kapsamdan geçer (<see cref="F5Shared.PickupOfficeScopeAsync"/>).</para>
///
/// <para><b>Tam değiştirme PUT'u</b> zorunlu <c>surum</c> ister (Postgres <c>xmin</c>); kilit altında uyuşmazlık →
/// 409 <c>cakisma</c>, hiçbir şey yazılmaz.</para>
///
/// <para><b>Çift gönderim:</b> rezervasyon defter yazmaz ve anahtar almaz (Blazor ile aynı): oluşturma iki kez
/// gönderilirse iki rezervasyon açılır (SPA düğmeyi istek boyunca kilitler); onay/iptal/kiraya çevirme durum
/// makinesiyle YAPISAL korunur (ikinci istek 400).</para>
///
/// <para><b>Para notu:</b> oluştur/güncelle fiyat motorunu (<c>PricingService</c>) tetikler — tutar/gün/KDV snapshot
/// SERVİSTE hesaplanır, uç formül taşımaz. Kiraya çevirme rezervasyonun fiyat taahhüdünü kiraya taşır ve sistem
/// ücret satırlarını uygular (<c>FeeLineService</c>, idempotent).</para>
/// </summary>
public static class ReservationApi
{
    private const string Root = UiApiExtensions.V1 + "/rezervasyonlar";

    public static RouteGroupBuilder MapReservationApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/rezervasyonlar")
            .WithTags("Rezervasyon")
            .RequirePermission(Permission.OperationsWrite);

        g.MapGet("", GetList).MapFields(ListRules);
        g.MapGet("/form-secenekleri", FormOptions);
        g.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create).MapFields(WriteRules);
        g.MapPut("/{id:guid}", Update).MapFields(WriteRules);
        g.MapPost("/{id:guid}/onayla", Confirm);
        g.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        g.MapPost("/{id:guid}/kiraya-cevir", ConvertToRental);
        return g;
    }

    // ================================================================== liste

    private static readonly SortFieldMap<RezervasyonListeSatiri> Map = SortFieldMap<RezervasyonListeSatiri>
        .Create(r => r.Id)
        .Alan("no", r => r.No)
        .Alan("musteri", r => r.MusteriAd)
        .Alan("plaka", r => r.Plaka)
        .Alan("basTar", r => r.BasTar)
        .Alan("bitTar", r => r.BitTar)
        .Alan("cikisOfisi", r => r.CikisOfisi)
        .Alan("kaynak", r => r.Kaynak)
        .Alan("gun", r => r.Gun)
        .Alan("tutar", r => r.Tutar)
        .Alan("durum", r => r.Durum);

    private static readonly (string, string)[] ListRules = F5Shared.SortRules;

    /// <summary>Blazor ReservationList süzgeçleri. Tarihler takvim günü (İstanbul); üst sınır GÜN DAHİL.</summary>
    public sealed class ReservationListFilter
    {
        /// <summary>Rez no / müşteri adı / plaka (içeren).</summary>
        [FromQuery(Name = "q")] public string? Q { get; set; }
        /// <summary>Durum adı: <c>Rezerv</c> | <c>Onayli</c> | <c>KirayaCevrildi</c> | <c>Iptal</c>.</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "basMin")] public DateOnly? BasMin { get; set; }
        [FromQuery(Name = "basMax")] public DateOnly? BasMax { get; set; }
        /// <summary>Rezervasyon kaynağı (tam eşleşme, harf duyarsız).</summary>
        [FromQuery(Name = "kaynak")] public string? Kaynak { get; set; }

        public ReservationFilter ToFilter()
        {
            var (min, max) = F5Shared.DayRange(BasMin, BasMax, "basMin", "basMax");
            return new ReservationFilter
            {
                Query = F5Shared.Nz(Q),
                Durum = F5Shared.EnumAdi<ReservationStatus>(Durum, "durum"),
                TarihMin = min,
                TarihMax = max,
                Kaynak = F5Shared.Nz(Kaynak),
            };
        }
    }

    private static async Task<Ok<Sayfa<RezervasyonListeSatiri>>> GetList(
        ReservationService rezervasyonlar, IDbContextFactory<AppDbContext> dbf, [AsParameters] ReservationListFilter f,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var rows = await rezervasyonlar.SearchAsync(f.ToFilter(), ct); // şube kapsamı serviste zorlanır
        // KVKK: ad + telefon MusteriGorunumu kuralından (AnonimAd/AnonimTelefon); satırdaki ham ad/telefon kullanılmaz.
        var customers = await F5Shared.CustomersAsync(dbf, rows.Select(s => s.Rez.MusteriId), ct);
        var list = rows.Select(s =>
        {
            var r = s.Rez;
            var account = customers.GetValueOrDefault(r.MusteriId);
            return new RezervasyonListeSatiri(r.Id, r.ReservationNo, r.MusteriId, account?.Ad ?? "—", account?.CepTel,
                r.VehicleId, s.Plaka, r.BasTar, r.BitTar, r.CikisOfisi, r.DonusOfisi, r.Kaynak, r.TalepTuru,
                r.GeldigiBirim, r.ProjeAdi, r.OnayKodu, r.Gun, r.Tutar, r.Durum.ToString());
        }).ToList();
        return TypedResults.Ok(F5Shared.Paginate(list, Map, sayfa, boyut, sirala));
    }

    /// <summary>Yeni rezervasyon formunun seçenekleri. <c>varsayilanFiyatTuru</c> YALNIZ yeni formun ön-seçimidir
    /// (FAZ-82; düzenlemede kaydın kendi değeri geçerli).</summary>
    private static async Task<Ok<RezervasyonFormSecenekleri>> FormOptions(FormDefaultResolver varsayilanlar, CancellationToken ct)
        => TypedResults.Ok(new RezervasyonFormSecenekleri(
            await varsayilanlar.PriceTypeAsync(ct), PriceTypeOption.All, RequestTypes));

    /// <summary>Talep türü önerileri (serbest metin; Blazor datalist'iyle aynı).</summary>
    public static readonly IReadOnlyList<string> RequestTypes = ["Bireysel", "Kurumsal", "Sigorta İkame", "Filo"];

    // ================================================================== detay

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Detail(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DetailResponseAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();

    /// <summary>Sürüm alanlardan ÖNCE okunur (F4.3 adversarial F2): arada yazım olursa istemcinin sürümü alanlarından
    /// ESKİ olur ve sonraki PUT güvenli tarafta (409) kalır.</summary>
    private static async Task<RezervasyonDetayYaniti?> DetailResponseAsync(
        Guid id, HttpContext http, ReservationService reservations, IBookingRepository store,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var version = await store.ReservationVersionAsync(id, ct);
        var r = await reservations.GetAsync(id, ct); // kapsam dışı → 403
        if (r is null) return null;
        var customers = await F5Shared.CustomersAsync(dbf, [r.MusteriId], ct);
        var plates = await F5Shared.PlatesAsync(dbf, [r.VehicleId], ct);
        var open = r.Durum is ReservationStatus.Rezerv or ReservationStatus.Onayli;
        var permission = new RezervasyonYetkileri(
            Duzenle: open,
            Onayla: r.Durum == ReservationStatus.Rezerv,
            KirayaCevir: open,
            Iptal: open && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete));
        return new RezervasyonDetayYaniti(
            RezervasyonDto.From(r, version), F5Shared.CustomerName(customers, r.MusteriId), F5Shared.Plate(plates, r.VehicleId), permission);
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Rezervasyon bulunamadı.");

    // ================================================================== yazmalar

    private static readonly (string, string)[] WriteRules =
    [
        ("Müşteri seçilmelidir", "musteriId"),
        ("Araç seçilmelidir", "vehicleId"),
        ("Bitiş tarihi başlangıçtan sonra", "bitTar"),
        ("Kira süresi en fazla", "bitTar"),
        ("Rezervasyon geçmiş tarihe", "basTar"),
        ("Rezervasyon en fazla 1 yıl", "basTar"),
        ("Günlük ücret negatif", "gunlukUcret"),
        ("Drop ücreti negatif", "dropUcreti"),
        ("Kampanya kodu", "kampanyaKodu"),
        ("Otomatik tarife bulunamadı", "gunlukUcret"),
        ("Talep türü en fazla", "talepTuru"),
        ("Geldiği birim en fazla", "geldigiBirim"),
        ("Onay kodu en fazla", "onayKodu"),
        ("Proje adı en fazla", "projeAdi"),
    ];

    /// <summary>Uç sınırları (F4.1 L3 dersi): <c>numeric(19,4)</c>/<c>numeric(9,4)</c> ve <c>varchar</c> taşması 500
    /// yerine 400 + <c>errors[alan]</c>. Süre üst sınırı kiranınkiyle aynı (rezervasyon kiraya çevrilir): yıl 9999 gibi
    /// bir bitiş gün × ücret çarpımını taşırır.</summary>
    internal static void Limit(RezervasyonIstegi i)
    {
        RentalLimits.Amount(i.GunlukUcret, "gunlukUcret", "Günlük ücret");
        RentalLimits.Amount(i.FazlaKmUcret, "fazlaKmUcret", "Fazla km ücreti");
        RentalLimits.Amount(i.YakitBirimUcret, "yakitBirimUcret", "Yakıt birim ücreti");
        RentalLimits.Amount(i.Provizyon, "provizyon", "Provizyon");
        RentalLimits.Amount(i.Depozito, "depozito", "Depozito");
        RentalLimits.Amount(i.KomisyonTutar, "komisyonTutar", "Komisyon tutarı");
        RentalLimits.Amount(i.DropUcreti, "dropUcreti", "Drop ücreti");
        RentalLimits.Amount(i.OtaKiraBedeli, "otaKiraBedeli", "Broker kira bedeli");
        RentalLimits.Amount(i.OtaDropBedeli, "otaDropBedeli", "Broker drop bedeli");
        RentalLimits.Amount(i.OtaBebekKoltugu, "otaBebekKoltugu", "Broker bebek koltuğu");
        RentalLimits.Amount(i.OtaNavigasyon, "otaNavigasyon", "Broker navigasyon");
        RentalLimits.Amount(i.OtaLcf, "otaLcf", "Broker LCF");
        RentalLimits.Amount(i.OtaCdw, "otaCdw", "Broker CDW");
        RentalLimits.Amount(i.OtaScdw, "otaScdw", "Broker SCDW");
        RentalLimits.Amount(i.OtaEkSurucu, "otaEkSurucu", "Broker ek sürücü");
        RentalLimits.Rate(i.KomisyonOran, "komisyonOran", "Komisyon oranı");
        RentalLimits.Rate(i.SonraOdeOran, "sonraOdeOran", "Sonra öde oranı");
        if (i.KmLimit is < 0 or > 10_000_000)
            throw new ValidationException("KM limiti 0 ile 10.000.000 arasında olmalıdır.", "kmLimit");
        RentalLimits.Text(i.CikisOfisi, 64, "cikisOfisi", "Çıkış ofisi");
        RentalLimits.Text(i.DonusOfisi, 64, "donusOfisi", "Dönüş ofisi");
        RentalLimits.Text(i.Aciklama, 1024, "aciklama", "Açıklama");
        RentalLimits.Text(i.Kaynak, 64, "kaynak", "Kaynak");
        RentalLimits.Text(i.KampanyaKodu, 64, "kampanyaKodu", "Kampanya kodu");
        RentalLimits.Text(i.FiyatTuru, 64, "fiyatTuru", "Fiyat türü");
        RentalLimits.Text(i.TalepTuru, 64, "talepTuru", "Talep türü");
        RentalLimits.Text(i.GeldigiBirim, 64, "geldigiBirim", "Geldiği birim");
        RentalLimits.Text(i.OnayKodu, 64, "onayKodu", "Onay kodu");
        RentalLimits.Text(i.ProjeAdi, 128, "projeAdi", "Proje adı");
        DatePolicy.RentalEnd(i.BasTar, i.BitTar);
    }

    private static async Task<Created<RezervasyonOlusturYaniti>> Create(
        RezervasyonIstegi istek, ReservationService rezervasyonlar, ILocationRepository lokasyonlar,
        ICurrentUser kullanici, CancellationToken ct)
    {
        Limit(istek);
        await F5Shared.PickupOfficeScopeAsync(lokasyonlar, kullanici, istek.CikisOfisi, ct);
        // Müşteri/araç varlık kontrolü ReservationService.CreateAsync girişinde (BookingPartyCheck; tek kural).
        var id = await rezervasyonlar.CreateAsync(istek.ToInput(), ct);
        var no = (await rezervasyonlar.GetAsync(id, ct))?.ReservationNo ?? "";
        return TypedResults.Created($"{Root}/{id}", new RezervasyonOlusturYaniti(id, no));
    }

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Update(
        Guid id, RezervasyonGuncelleIstegi istek, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        ILocationRepository lokasyonlar, ICurrentUser kullanici, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var existing = await rezervasyonlar.GetAsync(id, ct); // kapsam (403) → sonra her şey
        if (existing is null) return NotFoundProblem();
        if (string.IsNullOrWhiteSpace(istek.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        Limit(istek);
        // Çıkış ofisi DEĞİŞİYORSA hedef ofis de kapsamda olmalı (kira UpdateOpenAsync kuralı).
        if (!string.Equals(F5Shared.Nz(istek.CikisOfisi) ?? "", existing.CikisOfisi ?? "", StringComparison.Ordinal))
            await F5Shared.PickupOfficeScopeAsync(lokasyonlar, kullanici, istek.CikisOfisi, ct);
        // Varlık kontrolü ReservationService.UpdateAsync içinde (her yazımda).
        if (!await rezervasyonlar.UpdateAsync(id, istek.ToInput(), istek.Surum, ct)) return NotFoundProblem();
        return await DetailResponseAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();
    }

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Confirm(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await rezervasyonlar.GetAsync(id, ct) is null) return NotFoundProblem();
        if (!await rezervasyonlar.ConfirmAsync(id, ct)) return NotFoundProblem();
        return await DetailResponseAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();
    }

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Cancel(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await rezervasyonlar.GetAsync(id, ct) is null) return NotFoundProblem();
        if (!await rezervasyonlar.CancelAsync(id, ct)) return NotFoundProblem();
        return await DetailResponseAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : NotFoundProblem();
    }

    /// <summary>Kiraya çevir — yeni kira kimliği + sözleşme no (SPA kira formuna gider). Rezervasyonun fiyat taahhüdü
    /// yeniden fiyatlanmadan kiraya taşınır (servis kuralı).</summary>
    private static async Task<Results<Ok<KirayaCevirYaniti>, ProblemHttpResult>> ConvertToRental(
        Guid id, ReservationService rezervasyonlar, RentalService kiralar, CancellationToken ct)
    {
        if (await rezervasyonlar.GetAsync(id, ct) is null) return NotFoundProblem();
        var rentalId = await rezervasyonlar.ConvertToRentalAsync(id, ct);
        var no = (await kiralar.GetAsync(rentalId, ct))?.SozlesmeNo ?? "";
        return TypedResults.Ok(new KirayaCevirYaniti(rentalId, no));
    }
}

// ---------------------------------------------------------------- istek gövdeleri

/// <summary>
/// Rezervasyon alanları (<c>POST /rezervasyonlar</c>). Alan kümesi <see cref="ReservationService"/>'in yazdığı alanlarla
/// BİREBİR (whitelist) — PUT tam değiştirmedir: gövdede olmayan alan boş yazılır; SPA detaydaki değerleri geri gönderir.
/// </summary>
public record RezervasyonIstegi
{
    public required Guid MusteriId { get; init; }
    public required Guid VehicleId { get; init; }
    public required DateTimeOffset BasTar { get; init; }
    public required DateTimeOffset BitTar { get; init; }
    /// <summary>Boş/0 → tarife (fiyat motoru).</summary>
    public decimal? GunlukUcret { get; init; }
    public string? FiyatTuru { get; init; }
    public string? KampanyaKodu { get; init; }
    public string? CikisOfisi { get; init; }
    public string? DonusOfisi { get; init; }
    public string? Kaynak { get; init; }
    public string? Aciklama { get; init; }
    public int? KmLimit { get; init; }
    public decimal? FazlaKmUcret { get; init; }
    public decimal? YakitBirimUcret { get; init; }
    // ödeme/komisyon (bilgi; deftere/bakiyeye yansımaz)
    public decimal? Provizyon { get; init; }
    public decimal? Depozito { get; init; }
    public decimal? KomisyonOran { get; init; }
    public decimal? KomisyonTutar { get; init; }
    public decimal? DropUcreti { get; init; }
    public decimal? SonraOdeOran { get; init; }
    // brokerden gelen bilgi (FAZ 4.5; fiyata/deftere GİRMEZ)
    public decimal? OtaKiraBedeli { get; init; }
    public decimal? OtaDropBedeli { get; init; }
    public decimal? OtaBebekKoltugu { get; init; }
    public decimal? OtaNavigasyon { get; init; }
    public decimal? OtaLcf { get; init; }
    public decimal? OtaCdw { get; init; }
    public decimal? OtaScdw { get; init; }
    public decimal? OtaEkSurucu { get; init; }
    // talep/organizasyon (FAZ-48; kiraya çevirmede taşınır)
    public string? TalepTuru { get; init; }
    public string? GeldigiBirim { get; init; }
    public string? OnayKodu { get; init; }
    public string? ProjeAdi { get; init; }

    /// <summary>Servis girdisi. Metinler kırpılır (boş → null); anlar UTC'ye çevrilir.</summary>
    public BookingInput ToInput() => new()
    {
        MusteriId = MusteriId, VehicleId = VehicleId,
        BasTar = F5Shared.Utc(BasTar), BitTar = F5Shared.Utc(BitTar),
        GunlukUcret = GunlukUcret ?? 0m,
        FiyatTuru = F5Shared.Nz(FiyatTuru), KampanyaKodu = F5Shared.Nz(KampanyaKodu),
        CikisOfisi = F5Shared.Nz(CikisOfisi), DonusOfisi = F5Shared.Nz(DonusOfisi),
        Kaynak = F5Shared.Nz(Kaynak), Aciklama = F5Shared.Nz(Aciklama),
        KmLimit = KmLimit ?? 0, FazlaKmUcret = FazlaKmUcret ?? 0m, YakitBirimUcret = YakitBirimUcret ?? 0m,
        Provizyon = Provizyon, Depozito = Depozito, KomisyonOran = KomisyonOran, KomisyonTutar = KomisyonTutar,
        DropUcreti = DropUcreti, SonraOdeOran = SonraOdeOran,
        OtaKiraBedeli = OtaKiraBedeli, OtaDropBedeli = OtaDropBedeli, OtaBebekKoltugu = OtaBebekKoltugu,
        OtaNavigasyon = OtaNavigasyon, OtaLcf = OtaLcf, OtaCdw = OtaCdw, OtaScdw = OtaScdw, OtaEkSurucu = OtaEkSurucu,
        TalepTuru = F5Shared.Nz(TalepTuru), GeldigiBirim = F5Shared.Nz(GeldigiBirim),
        OnayKodu = F5Shared.Nz(OnayKodu), ProjeAdi = F5Shared.Nz(ProjeAdi),
    };
}

/// <summary><c>PUT /rezervasyonlar/{id}</c> — tam değiştirme; <c>surum</c> ZORUNLU (detay yanıtındaki değer).</summary>
public sealed record RezervasyonGuncelleIstegi : RezervasyonIstegi
{
    public string? Surum { get; init; }
}

// ---------------------------------------------------------------- yanıtlar

public sealed record RezervasyonListeSatiri(
    Guid Id, string No, Guid MusteriId, string MusteriAd, string? CepTel, Guid VehicleId, string Plaka,
    DateTimeOffset BasTar, DateTimeOffset BitTar, string? CikisOfisi, string? DonusOfisi, string? Kaynak,
    string? TalepTuru, string? GeldigiBirim, string? ProjeAdi, string? OnayKodu, int Gun, decimal Tutar, string Durum);

public sealed record RezervasyonFormSecenekleri(
    string? VarsayilanFiyatTuru, IReadOnlyList<string> FiyatTurleri, IReadOnlyList<string> TalepTurleri);

public sealed record RezervasyonYetkileri(bool Duzenle, bool Onayla, bool KirayaCevir, bool Iptal);

public sealed record RezervasyonDetayYaniti(RezervasyonDto Rezervasyon, string MusteriAd, string Plaka, RezervasyonYetkileri Yetkiler);

public sealed record RezervasyonOlusturYaniti(Guid Id, string No);

public sealed record KirayaCevirYaniti(Guid KiraId, string SozlesmeNo);

/// <summary>Rezervasyonun tüm düzenlenebilir alanları + servis hesapları (gün, tutar, döküm) + sürüm.</summary>
public sealed record RezervasyonDto(
    Guid Id, string No, string Durum, string? Surum, Guid MusteriId, Guid VehicleId,
    DateTimeOffset BasTar, DateTimeOffset BitTar, string? CikisOfisi, string? DonusOfisi,
    int Gun, decimal GunlukUcret, decimal Tutar, int? HediyeGun, int? FaturalananGun, decimal? IskontoTutar,
    decimal? HaftaSonuFark, string? FiyatTuru, string? KampanyaKodu, decimal? KdvOranSnapshot,
    int KmLimit, decimal FazlaKmUcret, decimal YakitBirimUcret,
    decimal? Provizyon, decimal? Depozito, decimal? KomisyonOran, decimal? KomisyonTutar, decimal? DropUcreti,
    decimal? SonraOdeOran, string? Kaynak, string? Aciklama,
    decimal? OtaKiraBedeli, decimal? OtaDropBedeli, decimal? OtaBebekKoltugu, decimal? OtaNavigasyon,
    decimal? OtaLcf, decimal? OtaCdw, decimal? OtaScdw, decimal? OtaEkSurucu,
    string? TalepTuru, string? GeldigiBirim, string? OnayKodu, string? ProjeAdi,
    Guid? KiraId, DateTimeOffset OlusturmaUtc)
{
    public static RezervasyonDto From(Reservation r, string? version) => new(
        r.Id, r.ReservationNo, r.Durum.ToString(), version, r.MusteriId, r.VehicleId, r.BasTar, r.BitTar,
        r.CikisOfisi, r.DonusOfisi, r.Gun, r.GunlukUcret, r.Tutar, r.HediyeGun, r.FaturalananGun, r.IskontoTutar,
        r.HaftaSonuFark, r.FiyatTuru, r.KampanyaKodu, r.KdvOranSnapshot, r.KmLimit, r.FazlaKmUcret, r.YakitBirimUcret,
        r.Provizyon, r.Depozito, r.KomisyonOran, r.KomisyonTutar, r.DropUcreti, r.SonraOdeOran, r.Kaynak, r.Aciklama,
        r.OtaKiraBedeli, r.OtaDropBedeli, r.OtaBebekKoltugu, r.OtaNavigasyon, r.OtaLcf, r.OtaCdw, r.OtaScdw,
        r.OtaEkSurucu, r.TalepTuru, r.GeldigiBirim, r.OnayKodu, r.ProjeAdi, r.RentalContractId, r.CreatedAtUtc);
}
