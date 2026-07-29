using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Fleet;

/// <summary>
/// PR-14 vitrin kartı — artık SINIF (VehicleGroup) değil İLAN (<see cref="WebIlan"/>) bazlı.
/// <paramref name="Adet"/>: Σ(VitrinAdet ?? 1) — YALNIZ gösterim, rezervasyon kapasitesi DEĞİL.
/// </summary>
public sealed record FleetShowcaseCard(
    Guid IlanId, string Slug, string Baslik, string? YilAralik, Guid? CoverPhotoId,
    decimal GunlukFiyat, bool KdvDahil, int Adet,
    IReadOnlyList<OzellikGoster> Ozellikler);

/// <summary>Sitede gösterilen teknik özellik (yalnız <c>Gorunur</c> olanlar taşınır).</summary>
public sealed record OzellikGoster(string Etiket, string Deger);

/// <summary>
/// PR-14: müsaitlik+fiyat arama sonucu (public). Fiyat İLANDAN gelir — <c>RentalQuoteEngine</c>
/// halka açık yoldan TAMAMEN ÇIKTI (kullanıcı kararı: "tarife matrisi ilk müşteriler için çok
/// karışık"). Gün sayısı yine <see cref="BookingMath.ComputeGun"/> ile hesaplanır — 24s blok +
/// tolerans mantığı yeniden YAZILMAZ, ERP ile aynı gün tanımı korunur.
/// </summary>
public sealed record PublicAvailabilityResult(
    Guid IlanId, string Slug, string Baslik, string? YilAralik, Guid? CoverPhotoId,
    int Gun, decimal GunlukFiyat, decimal Toplam, bool KdvDahil,
    /// <summary>Bu tarih aralığında MÜSAİT adet (Σ VitrinAdet ?? 1) — biri kirada ise düşer.</summary>
    int Adet,
    IReadOnlyList<OzellikGoster> Ozellikler);

public sealed record FleetShowcaseDetail(
    Guid IlanId, string Slug, string Baslik, string? YilAralik,
    decimal GunlukFiyat, decimal? HaftalikToplam, decimal? AylikToplam, bool KdvDahil,
    int Adet, IReadOnlyList<Guid> PhotoIds, IReadOnlyList<OzellikGoster> Ozellikler);

public sealed record FleetBranding(string? Marka, string? Adres, string? Tel, string? Email);

/// <summary>
/// Public-site filo vitrini. Yetki gerektirmez — guard-free okuma servisleri üstünden salt-okur
/// derleme (DashboardService deseni; `PublicTenantContext.Role=null` → BranchScope Unrestricted).
///
/// <para><b>PR-14 — vitrin İLAN bazlı.</b> Eskiden aktif her <see cref="VehicleGroup"/> bir kart
/// olurdu ve fiyat <c>RentalQuoteEngine</c>'den gelirdi. Artık kart = personelin sihirbazdan
/// yayınladığı ilan, fiyat = ilandaki sabit fiyat. Motor/tarife halka açık yoldan tamamen çıktı.</para>
///
/// <para><b>Yayın kapısı</b> (dört yüzeyin TEK kaynağı — vitrin/arama/detay/sitemap):
/// ilan <c>Yayinda</c> · <c>GunlukFiyat &gt; 0</c> · üye araçlardan en az birinin FOTOĞRAFI var ·
/// en az bir üye araç sitede gösterilebilir durumda (<c>!WebRezKapat</c> ve durumu Pasif/Satıldı değil).
/// Tarih şartı YOK — PR-11'in sezonluk-tarife penceresi karmaşası bu modelde ortadan kalktı.</para>
/// </summary>
public sealed class FleetShowcaseService(
    IWebIlanRepository ilanlar, VehiclePhotoService photos,
    IPublicBrandingRepository branding, ITenantContext tenant,
    AvailabilityService availability)
{
    /// <summary>Detay sayfasında gösterilecek en fazla fotoğraf (üye araç sayısı büyük olabilir).</summary>
    private const int MaxDetayFoto = 24;

    // ---- Yayın kapısı ----

    /// <summary>Sitede gösterilebilir üye araç: web'e kapalı ya da elden çıkmış araçlar SAYILMAZ.</summary>
    private static bool Gosterilebilir(Vehicle v)
        => !v.WebRezKapat && v.Durum != VehicleStatus.Pasif && v.Durum != VehicleStatus.Satildi;

    private sealed record Yayin(WebIlanDetay Detay, IReadOnlyList<Vehicle> Araclar, Vehicle Kapak);

    /// <summary>
    /// Yayındaki ilanlar + gösterilebilir üyeleri + kapak aracı. Foto varlığı TEK toplu sorguyla
    /// (ilan/araç başına sorgu, rate-limit'siz en sıcak anonim sayfada N+1 üretirdi).
    /// </summary>
    private async Task<List<Yayin>> YayindakilerAsync(CancellationToken ct)
    {
        var hepsi = await ilanlar.ListAsync(ct);
        var adaylar = hepsi
            .Where(d => d.Ilan.Durum == WebIlanDurum.Yayinda && d.Ilan.GunlukFiyat > 0m)
            .Select(d => (Detay: d, Araclar: d.Araclar.Where(Gosterilebilir).ToList()))
            .Where(x => x.Araclar.Count > 0)
            .ToList();
        if (adaylar.Count == 0) return [];

        var fotolu = await photos.ListVehicleIdsWithPhotoAsync(
            [.. adaylar.SelectMany(a => a.Araclar).Select(v => v.Id).Distinct()], ct);

        var sonuc = new List<Yayin>();
        foreach (var (detay, araclar) in adaylar)
        {
            // Kapak: fotoğrafı OLAN ilk araç. Kapak Id'si SNAPSHOT DEĞİL, her istekte hesaplanır —
            // foto silinirse kart kendini onarır (dangling GUID → kırık görsel olmaz).
            var kapak = araclar.FirstOrDefault(v => fotolu.Contains(v.Id));
            if (kapak is null) continue; // foto şartı
            sonuc.Add(new Yayin(detay, araclar, kapak));
        }
        // Sıra: WebIlan.Sira, sonra Baslik. Tie-break ŞART — eşit Sira'da Postgres sıra garanti
        // etmez (vitrin sırası her istekte değişir: SEO + test kararsızlığı).
        return [.. sonuc.OrderBy(y => y.Detay.Ilan.Sira).ThenBy(y => y.Detay.Ilan.Baslik, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<OzellikGoster> Gorunur(WebIlanDetay d)
        => [.. d.Ozellikler.Where(o => o.Gorunur).OrderBy(o => o.Sira).Select(o => new OzellikGoster(o.Etiket, o.Deger))];

    private static int Adet(IEnumerable<Vehicle> araclar) => araclar.Sum(v => v.VitrinAdet ?? 1);

    // ---- Vitrin ----

    public async Task<IReadOnlyList<FleetShowcaseCard>> ListShowcaseGroupsAsync(CancellationToken ct = default)
    {
        var kartlar = new List<FleetShowcaseCard>();
        foreach (var y in await YayindakilerAsync(ct))
        {
            var meta = await photos.ListMetaAsync(y.Kapak.Id, ct); // kapak aracında foto VAR
            kartlar.Add(new FleetShowcaseCard(
                y.Detay.Ilan.Id, y.Detay.Ilan.Slug, y.Detay.Ilan.Baslik, AracImza.YilAralik(y.Araclar),
                meta.Count > 0 ? meta[0].Id : null,
                y.Detay.Ilan.GunlukFiyat, y.Detay.Ilan.KdvDahil, Adet(y.Araclar), Gorunur(y.Detay)));
        }
        return kartlar;
    }

    /// <summary>PR-14: yayınlanmamış ilan <b>404</b> döner (null) — eski/paylaşılmış link
    /// fotosuz-fiyatsız içeriği ziyaretçiye AÇMAMALI; vitrin ile detayın yayın kararı TEK yerden.</summary>
    public async Task<FleetShowcaseDetail?> GetIlanDetayAsync(string slug, CancellationToken ct = default)
    {
        var y = (await YayindakilerAsync(ct)).FirstOrDefault(x => x.Detay.Ilan.Slug == slug);
        if (y is null) return null;

        var photoIds = new List<Guid>();
        foreach (var v in y.Araclar)
        {
            if (photoIds.Count >= MaxDetayFoto) break;
            photoIds.AddRange((await photos.ListMetaAsync(v.Id, ct)).Select(m => m.Id));
        }
        var i = y.Detay.Ilan;
        return new FleetShowcaseDetail(i.Id, i.Slug, i.Baslik, AracImza.YilAralik(y.Araclar),
            i.GunlukFiyat, i.HaftalikToplam, i.AylikToplam, i.KdvDahil,
            Adet(y.Araclar), [.. photoIds.Take(MaxDetayFoto)], Gorunur(y.Detay));
    }

    /// <summary>PR-14 geçiş: eski <c>/araclar/{groupId}</c> linkleri için ilan Id'siyle de çözülür
    /// (sitemap'te indekslenmiş adresler 404 olmasın — 301 ile slug'a yönlendirilir).</summary>
    public async Task<string?> SlugByIdAsync(Guid ilanId, CancellationToken ct = default)
        => (await YayindakilerAsync(ct)).FirstOrDefault(y => y.Detay.Ilan.Id == ilanId)?.Detay.Ilan.Slug;

    // ---- Arama ----

    /// <summary>
    /// PR-14 anonim müsaitlik+fiyat araması. Motor ÇAĞRILMAZ; fiyat ilandan gelir.
    ///
    /// <para><b>Gün kademesi:</b> 1–7 → <c>GunlukFiyat</c>, 8–29 → <c>HaftalikToplam/7</c>,
    /// 30+ → <c>AylikToplam/30</c>. Eşikler <c>RentalQuoteEngine.ResolveTierRate</c> ile AYNI
    /// (personelin ERP'de vereceği teklifle uyuşsun diye BİLİNÇLİ hizalama); üst kademe boşsa
    /// bir alta düşülür. DİKKAT: ilan alanları TOPLAM'dır, motorunki gibi günlük ücret değil.</para>
    /// </summary>
    public async Task<IReadOnlyList<PublicAvailabilityResult>> SearchAvailabilityAsync(
        DateTimeOffset from, DateTimeOffset to, string? sube, CancellationToken ct = default)
    {
        var musait = (await availability.FindAvailableAsync(from, to, null, sube, ct))
            .Where(v => !v.WebRezKapat).ToList();
        if (musait.Count == 0) return [];

        var gun = BookingMath.ComputeGun(from, to); // gün tanımı ERP ile ORTAK — yeniden yazılmaz
        var musaitIdler = musait.Select(v => v.Id).ToHashSet();
        var sonuclar = new List<PublicAvailabilityResult>();

        foreach (var y in await YayindakilerAsync(ct))
        {
            // Bu ilanın araçlarından bu tarih aralığında GERÇEKTEN müsait olanlar.
            var musaitUyeler = y.Araclar.Where(v => musaitIdler.Contains(v.Id)).ToList();
            if (musaitUyeler.Count == 0) continue;

            var gunluk = GunlukEsdeger(y.Detay.Ilan, gun);
            if (gunluk <= 0m) continue;

            var meta = await photos.ListMetaAsync(y.Kapak.Id, ct);
            var i = y.Detay.Ilan;
            sonuclar.Add(new PublicAvailabilityResult(
                i.Id, i.Slug, i.Baslik, AracImza.YilAralik(y.Araclar),
                meta.Count > 0 ? meta[0].Id : null,
                gun, gunluk, R(gunluk * gun), i.KdvDahil, Adet(musaitUyeler), Gorunur(y.Detay)));
        }
        return [.. sonuclar.OrderBy(r => r.GunlukFiyat)];
    }

    /// <summary>Gün sayısına düşen GÜNLÜK eşdeğer fiyat. Toplam alanları /7 ve /30 ile günlüğe çevrilir.</summary>
    internal static decimal GunlukEsdeger(WebIlan ilan, int gun)
    {
        if (gun >= 30 && ilan.AylikToplam is { } ay && ay > 0m) return R(ay / 30m);
        if (gun >= 8 && ilan.HaftalikToplam is { } hafta && hafta > 0m) return R(hafta / 7m);
        // Üst kademe girilmemişse bir alta düş (motorun `?? m.GunHaftalik` davranışıyla aynı).
        if (gun >= 30 && ilan.HaftalikToplam is { } h2 && h2 > 0m) return R(h2 / 7m);
        return ilan.GunlukFiyat;
    }

    private static decimal R(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

    // ---- Marka / SEO ----

    public Task<FleetBranding> GetBrandingAsync(CancellationToken ct = default)
        => branding.GetAsync(tenant.TenantIdOrThrow(), ct);

    /// <summary>PR-9: SEO kanonik host'u (canonical link + sitemap + robots TEK kaynağı).</summary>
    public Task<string?> GetCanonicalHostAsync(CancellationToken ct = default)
        => branding.GetCanonicalHostAsync(tenant.TenantIdOrThrow(), ct);
}
