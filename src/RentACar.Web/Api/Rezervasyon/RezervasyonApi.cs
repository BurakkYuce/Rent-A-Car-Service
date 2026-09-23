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
/// metniyle sızmaz). Oluştur/güncellemede çıkış ofisi GİRİŞTE kapsamdan geçer (<see cref="F5Ortak.CikisOfisiKapsamiAsync"/>).</para>
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
public static class RezervasyonApi
{
    private const string Kok = UiApiExtensions.V1 + "/rezervasyonlar";

    public static RouteGroupBuilder MapRezervasyonApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/rezervasyonlar")
            .WithTags("Rezervasyon")
            .RequirePermission(Permission.OperationsWrite);

        g.MapGet("", Liste).AlanlariEsle(ListeKurallari);
        g.MapGet("/form-secenekleri", FormSecenekleri);
        g.MapGet("/{id:guid}", Detay);
        g.MapPost("", Olustur).AlanlariEsle(YazmaKurallari);
        g.MapPut("/{id:guid}", Guncelle).AlanlariEsle(YazmaKurallari);
        g.MapPost("/{id:guid}/onayla", Onayla);
        g.MapPost("/{id:guid}/iptal", Iptal).RequirePermission(Permission.OperationsDelete);
        g.MapPost("/{id:guid}/kiraya-cevir", KirayaCevir);
        return g;
    }

    // ================================================================== liste

    private static readonly SiralamaHaritasi<RezervasyonListeSatiri> Harita = SiralamaHaritasi<RezervasyonListeSatiri>
        .Olustur(r => r.Id)
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

    private static readonly (string, string)[] ListeKurallari = F5Ortak.SiralamaKurallari;

    /// <summary>Blazor ReservationList süzgeçleri. Tarihler takvim günü (İstanbul); üst sınır GÜN DAHİL.</summary>
    public sealed class RezervasyonListeFiltresi
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
            var (min, max) = F5Ortak.GunAraligi(BasMin, BasMax, "basMin", "basMax");
            return new ReservationFilter
            {
                Query = F5Ortak.Nz(Q),
                Durum = F5Ortak.EnumAdi<ReservationStatus>(Durum, "durum"),
                TarihMin = min,
                TarihMax = max,
                Kaynak = F5Ortak.Nz(Kaynak),
            };
        }
    }

    private static async Task<Ok<Sayfa<RezervasyonListeSatiri>>> Liste(
        ReservationService rezervasyonlar, IDbContextFactory<AppDbContext> dbf, [AsParameters] RezervasyonListeFiltresi f,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var satirlar = await rezervasyonlar.SearchAsync(f.ToFilter(), ct); // şube kapsamı serviste zorlanır
        // KVKK: ad + telefon MusteriGorunumu kuralından (AnonimAd/AnonimTelefon); satırdaki ham ad/telefon kullanılmaz.
        var cariler = await F5Ortak.CarilerAsync(dbf, satirlar.Select(s => s.Rez.MusteriId), ct);
        var liste = satirlar.Select(s =>
        {
            var r = s.Rez;
            var cari = cariler.GetValueOrDefault(r.MusteriId);
            return new RezervasyonListeSatiri(r.Id, r.ReservationNo, r.MusteriId, cari?.Ad ?? "—", cari?.CepTel,
                r.VehicleId, s.Plaka, r.BasTar, r.BitTar, r.CikisOfisi, r.DonusOfisi, r.Kaynak, r.TalepTuru,
                r.GeldigiBirim, r.ProjeAdi, r.OnayKodu, r.Gun, r.Tutar, r.Durum.ToString());
        }).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(liste, Harita, sayfa, boyut, sirala));
    }

    /// <summary>Yeni rezervasyon formunun seçenekleri. <c>varsayilanFiyatTuru</c> YALNIZ yeni formun ön-seçimidir
    /// (FAZ-82; düzenlemede kaydın kendi değeri geçerli).</summary>
    private static async Task<Ok<RezervasyonFormSecenekleri>> FormSecenekleri(FormVarsayilanCozucu varsayilanlar, CancellationToken ct)
        => TypedResults.Ok(new RezervasyonFormSecenekleri(
            await varsayilanlar.FiyatTuruAsync(ct), FiyatTuruSecenek.Hepsi, TalepTurleri));

    /// <summary>Talep türü önerileri (serbest metin; Blazor datalist'iyle aynı).</summary>
    public static readonly IReadOnlyList<string> TalepTurleri = ["Bireysel", "Kurumsal", "Sigorta İkame", "Filo"];

    // ================================================================== detay

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Detay(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DetayYanitiAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();

    /// <summary>Sürüm alanlardan ÖNCE okunur (F4.3 adversarial F2): arada yazım olursa istemcinin sürümü alanlarından
    /// ESKİ olur ve sonraki PUT güvenli tarafta (409) kalır.</summary>
    private static async Task<RezervasyonDetayYaniti?> DetayYanitiAsync(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var surum = await depo.ReservationSurumuAsync(id, ct);
        var r = await rezervasyonlar.GetAsync(id, ct); // kapsam dışı → 403
        if (r is null) return null;
        var cariler = await F5Ortak.CarilerAsync(dbf, [r.MusteriId], ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, [r.VehicleId], ct);
        var acik = r.Durum is ReservationStatus.Rezerv or ReservationStatus.Onayli;
        var yetki = new RezervasyonYetkileri(
            Duzenle: acik,
            Onayla: r.Durum == ReservationStatus.Rezerv,
            KirayaCevir: acik,
            Iptal: acik && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete));
        return new RezervasyonDetayYaniti(
            RezervasyonDto.From(r, surum), F5Ortak.CariAdi(cariler, r.MusteriId), F5Ortak.Plaka(plakalar, r.VehicleId), yetki);
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Rezervasyon bulunamadı.");

    // ================================================================== yazmalar

    private static readonly (string, string)[] YazmaKurallari =
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
    internal static void Sinirla(RezervasyonIstegi i)
    {
        Sinirlar.Tutar(i.GunlukUcret, "gunlukUcret", "Günlük ücret");
        Sinirlar.Tutar(i.FazlaKmUcret, "fazlaKmUcret", "Fazla km ücreti");
        Sinirlar.Tutar(i.YakitBirimUcret, "yakitBirimUcret", "Yakıt birim ücreti");
        Sinirlar.Tutar(i.Provizyon, "provizyon", "Provizyon");
        Sinirlar.Tutar(i.Depozito, "depozito", "Depozito");
        Sinirlar.Tutar(i.KomisyonTutar, "komisyonTutar", "Komisyon tutarı");
        Sinirlar.Tutar(i.DropUcreti, "dropUcreti", "Drop ücreti");
        Sinirlar.Tutar(i.OtaKiraBedeli, "otaKiraBedeli", "Broker kira bedeli");
        Sinirlar.Tutar(i.OtaDropBedeli, "otaDropBedeli", "Broker drop bedeli");
        Sinirlar.Tutar(i.OtaBebekKoltugu, "otaBebekKoltugu", "Broker bebek koltuğu");
        Sinirlar.Tutar(i.OtaNavigasyon, "otaNavigasyon", "Broker navigasyon");
        Sinirlar.Tutar(i.OtaLcf, "otaLcf", "Broker LCF");
        Sinirlar.Tutar(i.OtaCdw, "otaCdw", "Broker CDW");
        Sinirlar.Tutar(i.OtaScdw, "otaScdw", "Broker SCDW");
        Sinirlar.Tutar(i.OtaEkSurucu, "otaEkSurucu", "Broker ek sürücü");
        Sinirlar.Oran(i.KomisyonOran, "komisyonOran", "Komisyon oranı");
        Sinirlar.Oran(i.SonraOdeOran, "sonraOdeOran", "Sonra öde oranı");
        if (i.KmLimit is < 0 or > 10_000_000)
            throw new ValidationException("KM limiti 0 ile 10.000.000 arasında olmalıdır.", "kmLimit");
        Sinirlar.Metin(i.CikisOfisi, 64, "cikisOfisi", "Çıkış ofisi");
        Sinirlar.Metin(i.DonusOfisi, 64, "donusOfisi", "Dönüş ofisi");
        Sinirlar.Metin(i.Aciklama, 1024, "aciklama", "Açıklama");
        Sinirlar.Metin(i.Kaynak, 64, "kaynak", "Kaynak");
        Sinirlar.Metin(i.KampanyaKodu, 64, "kampanyaKodu", "Kampanya kodu");
        Sinirlar.Metin(i.FiyatTuru, 64, "fiyatTuru", "Fiyat türü");
        Sinirlar.Metin(i.TalepTuru, 64, "talepTuru", "Talep türü");
        Sinirlar.Metin(i.GeldigiBirim, 64, "geldigiBirim", "Geldiği birim");
        Sinirlar.Metin(i.OnayKodu, 64, "onayKodu", "Onay kodu");
        Sinirlar.Metin(i.ProjeAdi, 128, "projeAdi", "Proje adı");
        TarihPolitikasi.KiraBitis(i.BasTar, i.BitTar);
    }

    /// <summary>
    /// Müşteri ve araç bu kiracıda VAR olmalı (F4.1 adversarial L5): <c>Reservations.MusteriId/VehicleId</c>'de FK yok —
    /// başka kiracının ya da hiç olmayan kimlikle kayıt yazılabiliyordu (RLS kapsamlı okuma).
    /// </summary>
    internal static async Task VarlikAsync(
        ICustomerRepository musteriler, IVehicleRepository araclar, Guid musteriId, Guid vehicleId, CancellationToken ct)
    {
        if (await musteriler.FindAsync(musteriId, ct) is null)
            throw new ValidationException("Müşteri bulunamadı.", "musteriId");
        if (await araclar.FindAsync(vehicleId, ct) is null)
            throw new ValidationException("Araç bulunamadı.", "vehicleId");
    }

    private static async Task<Created<RezervasyonOlusturYaniti>> Olustur(
        RezervasyonIstegi istek, ReservationService rezervasyonlar, ICustomerRepository musteriler,
        IVehicleRepository araclar, ILocationRepository lokasyonlar, ICurrentUser kullanici, CancellationToken ct)
    {
        Sinirla(istek);
        await F5Ortak.CikisOfisiKapsamiAsync(lokasyonlar, kullanici, istek.CikisOfisi, ct);
        await VarlikAsync(musteriler, araclar, istek.MusteriId, istek.VehicleId, ct);
        var id = await rezervasyonlar.CreateAsync(istek.ToInput(), ct);
        var no = (await rezervasyonlar.GetAsync(id, ct))?.ReservationNo ?? "";
        return TypedResults.Created($"{Kok}/{id}", new RezervasyonOlusturYaniti(id, no));
    }

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Guncelle(
        Guid id, RezervasyonGuncelleIstegi istek, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        ICustomerRepository musteriler, IVehicleRepository araclar, ILocationRepository lokasyonlar, ICurrentUser kullanici,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var mevcut = await rezervasyonlar.GetAsync(id, ct); // kapsam (403) → sonra her şey
        if (mevcut is null) return Bulunamadi();
        if (string.IsNullOrWhiteSpace(istek.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        Sinirla(istek);
        // Çıkış ofisi DEĞİŞİYORSA hedef ofis de kapsamda olmalı (kira UpdateOpenAsync kuralı).
        if (!string.Equals(F5Ortak.Nz(istek.CikisOfisi) ?? "", mevcut.CikisOfisi ?? "", StringComparison.Ordinal))
            await F5Ortak.CikisOfisiKapsamiAsync(lokasyonlar, kullanici, istek.CikisOfisi, ct);
        await VarlikAsync(musteriler, araclar, istek.MusteriId, istek.VehicleId, ct);
        if (!await rezervasyonlar.UpdateAsync(id, istek.ToInput(), istek.Surum, ct)) return Bulunamadi();
        return await DetayYanitiAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();
    }

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Onayla(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await rezervasyonlar.GetAsync(id, ct) is null) return Bulunamadi();
        if (!await rezervasyonlar.ConfirmAsync(id, ct)) return Bulunamadi();
        return await DetayYanitiAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();
    }

    private static async Task<Results<Ok<RezervasyonDetayYaniti>, ProblemHttpResult>> Iptal(
        Guid id, HttpContext http, ReservationService rezervasyonlar, IBookingRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await rezervasyonlar.GetAsync(id, ct) is null) return Bulunamadi();
        if (!await rezervasyonlar.CancelAsync(id, ct)) return Bulunamadi();
        return await DetayYanitiAsync(id, http, rezervasyonlar, depo, dbf, ct) is { } y ? TypedResults.Ok(y) : Bulunamadi();
    }

    /// <summary>Kiraya çevir — yeni kira kimliği + sözleşme no (SPA kira formuna gider). Rezervasyonun fiyat taahhüdü
    /// yeniden fiyatlanmadan kiraya taşınır (servis kuralı).</summary>
    private static async Task<Results<Ok<KirayaCevirYaniti>, ProblemHttpResult>> KirayaCevir(
        Guid id, ReservationService rezervasyonlar, RentalService kiralar, CancellationToken ct)
    {
        if (await rezervasyonlar.GetAsync(id, ct) is null) return Bulunamadi();
        var kiraId = await rezervasyonlar.ConvertToRentalAsync(id, ct);
        var no = (await kiralar.GetAsync(kiraId, ct))?.SozlesmeNo ?? "";
        return TypedResults.Ok(new KirayaCevirYaniti(kiraId, no));
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
        BasTar = F5Ortak.Utc(BasTar), BitTar = F5Ortak.Utc(BitTar),
        GunlukUcret = GunlukUcret ?? 0m,
        FiyatTuru = F5Ortak.Nz(FiyatTuru), KampanyaKodu = F5Ortak.Nz(KampanyaKodu),
        CikisOfisi = F5Ortak.Nz(CikisOfisi), DonusOfisi = F5Ortak.Nz(DonusOfisi),
        Kaynak = F5Ortak.Nz(Kaynak), Aciklama = F5Ortak.Nz(Aciklama),
        KmLimit = KmLimit ?? 0, FazlaKmUcret = FazlaKmUcret ?? 0m, YakitBirimUcret = YakitBirimUcret ?? 0m,
        Provizyon = Provizyon, Depozito = Depozito, KomisyonOran = KomisyonOran, KomisyonTutar = KomisyonTutar,
        DropUcreti = DropUcreti, SonraOdeOran = SonraOdeOran,
        OtaKiraBedeli = OtaKiraBedeli, OtaDropBedeli = OtaDropBedeli, OtaBebekKoltugu = OtaBebekKoltugu,
        OtaNavigasyon = OtaNavigasyon, OtaLcf = OtaLcf, OtaCdw = OtaCdw, OtaScdw = OtaScdw, OtaEkSurucu = OtaEkSurucu,
        TalepTuru = F5Ortak.Nz(TalepTuru), GeldigiBirim = F5Ortak.Nz(GeldigiBirim),
        OnayKodu = F5Ortak.Nz(OnayKodu), ProjeAdi = F5Ortak.Nz(ProjeAdi),
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
    public static RezervasyonDto From(Reservation r, string? surum) => new(
        r.Id, r.ReservationNo, r.Durum.ToString(), surum, r.MusteriId, r.VehicleId, r.BasTar, r.BitTar,
        r.CikisOfisi, r.DonusOfisi, r.Gun, r.GunlukUcret, r.Tutar, r.HediyeGun, r.FaturalananGun, r.IskontoTutar,
        r.HaftaSonuFark, r.FiyatTuru, r.KampanyaKodu, r.KdvOranSnapshot, r.KmLimit, r.FazlaKmUcret, r.YakitBirimUcret,
        r.Provizyon, r.Depozito, r.KomisyonOran, r.KomisyonTutar, r.DropUcreti, r.SonraOdeOran, r.Kaynak, r.Aciklama,
        r.OtaKiraBedeli, r.OtaDropBedeli, r.OtaBebekKoltugu, r.OtaNavigasyon, r.OtaLcf, r.OtaCdw, r.OtaScdw,
        r.OtaEkSurucu, r.TalepTuru, r.GeldigiBirim, r.OnayKodu, r.ProjeAdi, r.RentalContractId, r.CreatedAtUtc);
}
