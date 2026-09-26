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
/// karışık"). Gün sayısı yine <see cref="BookingMath.ComputeDays"/> ile hesaplanır — 24s blok +
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

/// <summary>
/// Halka açık sitede gösterilen firma bilgisi. PR-16: <see cref="MobilTel"/> + <see cref="WhatsApp"/>
/// eklendi — iletişim sayfası ve WhatsApp CTA'sı için (Türkiye'de en çok kullanılan temas kanalı).
/// Bu alanların hepsi zaten fatura/sözleşme başlığında müşteriye görünüyor, yeni PII yüzeyi YOK.
/// </summary>
public sealed record FleetBranding(string? Marka, string? Adres, string? Tel, string? Email,
    string? MobilTel = null, string? WhatsApp = null);

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
    IWebListingRepository listings, VehiclePhotoService photos,
    IPublicBrandingRepository branding, ITenantContext tenant,
    AvailabilityService availability)
{
    /// <summary>Detay sayfasında gösterilecek en fazla fotoğraf (üye araç sayısı büyük olabilir).</summary>
    private const int MaxDetailPhotos = 24;

    // ---- Yayın kapısı ----

    /// <summary>Sitede gösterilebilir üye araç: web'e kapalı ya da elden çıkmış araçlar SAYILMAZ.</summary>
    private static bool IsDisplayable(Vehicle v)
        => !v.WebRezKapat && v.Durum != VehicleStatus.Pasif && v.Durum != VehicleStatus.Satildi;

    private sealed record Yayin(WebIlanDetay Detay, IReadOnlyList<Vehicle> Araclar, Vehicle Kapak);

    /// <summary>
    /// Yayındaki ilanlar + gösterilebilir üyeleri + kapak aracı. Foto varlığı TEK toplu sorguyla
    /// (ilan/araç başına sorgu, rate-limit'siz en sıcak anonim sayfada N+1 üretirdi).
    /// </summary>
    private async Task<List<Yayin>> PublishedAsync(CancellationToken ct)
    {
        var all = await listings.ListAsync(ct);
        var candidates = all
            .Where(d => d.Ilan.Durum == WebIlanDurum.Yayinda && d.Ilan.GunlukFiyat > 0m)
            .Select(d => (Detay: d, Araclar: d.Araclar.Where(IsDisplayable).ToList()))
            .Where(x => x.Araclar.Count > 0)
            .ToList();
        if (candidates.Count == 0) return [];

        var withPhoto = await photos.ListVehicleIdsWithPhotoAsync(
            [.. candidates.SelectMany(a => a.Araclar).Select(v => v.Id).Distinct()], ct);

        var result = new List<Yayin>();
        foreach (var (detail, vehicles) in candidates)
        {
            // Kapak: fotoğrafı OLAN ilk araç. Kapak Id'si SNAPSHOT DEĞİL, her istekte hesaplanır —
            // foto silinirse kart kendini onarır (dangling GUID → kırık görsel olmaz).
            var cover = vehicles.FirstOrDefault(v => withPhoto.Contains(v.Id));
            if (cover is null) continue; // foto şartı
            result.Add(new Yayin(detail, vehicles, cover));
        }
        // Sıra: WebIlan.Sira, sonra Baslik. Tie-break ŞART — eşit Sira'da Postgres sıra garanti
        // etmez (vitrin sırası her istekte değişir: SEO + test kararsızlığı).
        return [.. result.OrderBy(y => y.Detay.Ilan.Sira).ThenBy(y => y.Detay.Ilan.Baslik, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<OzellikGoster> Visible(WebIlanDetay d)
        => [.. d.Ozellikler.Where(o => o.Gorunur).OrderBy(o => o.Sira).Select(o => new OzellikGoster(o.Etiket, o.Deger))];

    /// <summary>
    /// KART çipleri: görünür özelliklerden, değeri BAŞLIKTA zaten geçenler atılır.
    ///
    /// <para><b>Neden:</b> sihirbaz özellikleri araç kaydından tohumluyor (Marka/Model/Vites/Yıl/Renk)
    /// ve kart başlığı da <c>AracImza.Baslik</c> ile aynı üç alandan kuruluyor. Sonuç: "Fiat Egea
    /// Manuel" başlığının altında "Fiat", "Egea", "Manuel" çipleri — kart yalnız İLK ÜÇ çipi
    /// gösterdiği için ziyaretçiye hiçbir YENİ bilgi kalmıyordu (yıl, renk, bagaj hep kesiliyordu).
    /// Ayıklama yalnız KARTA uygulanır; DETAY sayfasının teknik özellik tablosu tam listeyi
    /// gösterir — orada Marka/Model satırı bilgi olarak yerinde.</para>
    ///
    /// <para><b>Kelime-bazlı karşılaştırma</b> (alt-dize değil): "Manuel" başlıkta bir KELİME olarak
    /// geçiyorsa atılır, ama "2023"/"BEYAZ"/"510 litre" kalır. Alt-dize kullansaydık kısa bir değer
    /// ("an" gibi) alakasız bir başlık kelimesinin içinde bulunup sessizce düşerdi.</para>
    /// </summary>
    private static IReadOnlyList<OzellikGoster> CardChips(WebIlanDetay d, string title)
    {
        var leadingWords = TurkishText.Normalize(title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        return [.. Visible(d).Where(o =>
        {
            var words = TurkishText.Normalize(o.Deger ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // Boş değer çip olmaz; TÜM kelimeleri başlıkta geçen değer tekrardır.
            return words.Length > 0 && !words.All(leadingWords.Contains);
        })];
    }

    private static int Count(IEnumerable<Vehicle> vehicles) => vehicles.Sum(v => v.VitrinAdet ?? 1);

    // ---- Vitrin ----

    public async Task<IReadOnlyList<FleetShowcaseCard>> ListShowcaseGroupsAsync(CancellationToken ct = default)
    {
        var cards = new List<FleetShowcaseCard>();
        foreach (var y in await PublishedAsync(ct))
        {
            var meta = await photos.ListMetaAsync(y.Kapak.Id, ct); // kapak aracında foto VAR
            cards.Add(new FleetShowcaseCard(
                y.Detay.Ilan.Id, y.Detay.Ilan.Slug, y.Detay.Ilan.Baslik, VehicleSignature.YearRange(y.Araclar),
                meta.Count > 0 ? meta[0].Id : null,
                y.Detay.Ilan.GunlukFiyat, y.Detay.Ilan.KdvDahil, Count(y.Araclar),
                CardChips(y.Detay, y.Detay.Ilan.Baslik)));
        }
        return cards;
    }

    /// <summary>PR-14: yayınlanmamış ilan <b>404</b> döner (null) — eski/paylaşılmış link
    /// fotosuz-fiyatsız içeriği ziyaretçiye AÇMAMALI; vitrin ile detayın yayın kararı TEK yerden.</summary>
    public async Task<FleetShowcaseDetail?> GetListingDetailAsync(string slug, CancellationToken ct = default)
    {
        var y = (await PublishedAsync(ct)).FirstOrDefault(x => x.Detay.Ilan.Slug == slug);
        if (y is null) return null;

        var photoIds = new List<Guid>();
        foreach (var v in y.Araclar)
        {
            if (photoIds.Count >= MaxDetailPhotos) break;
            photoIds.AddRange((await photos.ListMetaAsync(v.Id, ct)).Select(m => m.Id));
        }
        var i = y.Detay.Ilan;
        return new FleetShowcaseDetail(i.Id, i.Slug, i.Baslik, VehicleSignature.YearRange(y.Araclar),
            i.GunlukFiyat, i.HaftalikToplam, i.AylikToplam, i.KdvDahil,
            Count(y.Araclar), [.. photoIds.Take(MaxDetailPhotos)], Visible(y.Detay));
    }

    /// <summary>PR-14 geçiş: eski <c>/araclar/{groupId}</c> linkleri için ilan Id'siyle de çözülür
    /// (sitemap'te indekslenmiş adresler 404 olmasın — 301 ile slug'a yönlendirilir).</summary>
    public async Task<string?> SlugByIdAsync(Guid listingId, CancellationToken ct = default)
        => (await PublishedAsync(ct)).FirstOrDefault(y => y.Detay.Ilan.Id == listingId)?.Detay.Ilan.Slug;

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
        DateTimeOffset from, DateTimeOffset to, string? branch, CancellationToken ct = default)
    {
        var available = (await availability.FindAvailableAsync(from, to, null, branch, ct))
            .Where(v => !v.WebRezKapat).ToList();
        if (available.Count == 0) return [];

        var day = BookingMath.ComputeDays(from, to); // gün tanımı ERP ile ORTAK — yeniden yazılmaz
        var availableIds = available.Select(v => v.Id).ToHashSet();
        var results = new List<PublicAvailabilityResult>();

        foreach (var y in await PublishedAsync(ct))
        {
            // Bu ilanın araçlarından bu tarih aralığında GERÇEKTEN müsait olanlar.
            var availableMembers = y.Araclar.Where(v => availableIds.Contains(v.Id)).ToList();
            if (availableMembers.Count == 0) continue;

            var daily = DailyEquivalent(y.Detay.Ilan, day);
            if (daily <= 0m) continue;

            var meta = await photos.ListMetaAsync(y.Kapak.Id, ct);
            var i = y.Detay.Ilan;
            results.Add(new PublicAvailabilityResult(
                i.Id, i.Slug, i.Baslik, VehicleSignature.YearRange(y.Araclar),
                meta.Count > 0 ? meta[0].Id : null,
                day, daily, R(daily * day), i.KdvDahil, Count(availableMembers),
                CardChips(y.Detay, i.Baslik)));
        }
        return [.. results.OrderBy(r => r.GunlukFiyat)];
    }

    /// <summary>Gün sayısına düşen GÜNLÜK eşdeğer fiyat. Toplam alanları /7 ve /30 ile günlüğe çevrilir.</summary>
    internal static decimal DailyEquivalent(WebIlan listing, int day)
    {
        if (day >= 30 && listing.AylikToplam is { } month && month > 0m) return R(month / 30m);
        if (day >= 8 && listing.HaftalikToplam is { } week && week > 0m) return R(week / 7m);
        // Üst kademe girilmemişse bir alta düş (motorun `?? m.GunHaftalik` davranışıyla aynı).
        if (day >= 30 && listing.HaftalikToplam is { } h2 && h2 > 0m) return R(h2 / 7m);
        return listing.GunlukFiyat;
    }

    private static decimal R(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

    // ---- Marka / SEO ----
    //
    // PR-19 — İSTEK-İÇİ ÖNBELLEK. Ölçümde görüldü: tek bir halka açık sayfa isteğinde marka
    // BİRDEN ÇOK kez çekiliyor (kabuk üst barı, alt bilgi, sayfa başlığı, JSON-LD…); kanonik host
    // da hem `App.razor`ın canonical etiketi hem yapısal veri için isteniyor. Servis `AddScoped`
    // olduğundan örnek istek başına tek → sonraki çağrılar DB'ye gitmez. Veri istek ortasında
    // değişmez, tutarlılık riski yok.
    private FleetBranding? _brandingCache;
    private bool _canonicalCache;
    private string? _canonicalValue;

    public async Task<FleetBranding> GetBrandingAsync(CancellationToken ct = default)
        => _brandingCache ??= await branding.GetAsync(tenant.TenantIdOrThrow(), ct);

    /// <summary>PR-9: SEO kanonik host'u (canonical link + sitemap + robots TEK kaynağı).</summary>
    public async Task<string?> GetCanonicalHostAsync(CancellationToken ct = default)
    {
        // null da GEÇERLİ bir sonuç (site hiç yayında değil) → ayrı bir "çekildi mi" bayrağı gerekir.
        if (_canonicalCache) return _canonicalValue;
        _canonicalValue = await branding.GetCanonicalHostAsync(tenant.TenantIdOrThrow(), ct);
        _canonicalCache = true;
        return _canonicalValue;
    }
}
