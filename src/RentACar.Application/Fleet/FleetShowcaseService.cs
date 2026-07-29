using RentACar.Application.Availability;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Pricing;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Fleet;

/// <summary>PR-11 <paramref name="Adet"/>: vitrinde gösterilecek araç sayısı = Σ(VitrinAdet ?? 1).
/// YALNIZ gösterim — rezervasyon kapasitesi DEĞİL (bkz. <see cref="Vehicle.VitrinAdet"/>).</summary>
public sealed record FleetShowcaseCard(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, Guid? CoverPhotoId, int Adet = 1);

/// <summary>
/// PR-7: müsaitlik+fiyat arama sonucu (public). TÜM sayısal değerler fiyat MOTORUNDAN okunur —
/// gün sayısı da toplam da BURADA HESAPLANMAZ: rent-a-car'da "gün" tanımı (24s blok + tolerans,
/// saat bileşeni) ve toplam (hafta sonu farkı, iskonto, hediye gün) motorun içindedir; ikinci bir
/// formül personelin verdiği teklifle uyuşmazlık üretirdi. `MusaitlikArama.razor` da aynı şekilde
/// `q.GunlukUcret`/`q.GenelToplam` okur (çarpma YAPMAZ).
///
/// KDV: motor NET (KDV hariç) döndürür; tüketiciye KDV DAHİL göstermek için `KdvVarsayilan.OranAsync`
/// ile brüte çevrilir — yalnız GÖSTERİM amaçlı gösterge rakam (gerçek rezervasyonda KDV normal
/// zincirinden yeniden hesaplanır).
/// </summary>
public sealed record PublicAvailabilityResult(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, Guid? CoverPhotoId,
    string GrupKod, int Gun, string ParaBirimi,
    decimal GunlukUcretKdvHaric, decimal GunlukUcretKdvDahil,
    decimal ToplamKdvHaric, decimal ToplamKdvDahil,
    /// <summary>PR-11: bu tarih aralığında MÜSAİT araç adedi (Σ VitrinAdet ?? 1) — biri kirada ise düşer.</summary>
    int Adet = 1);

public sealed record FleetShowcaseDetail(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, IReadOnlyList<Guid> PhotoIds, int Adet = 1);

public sealed record FleetBranding(string? Marka, string? Adres, string? Tel, string? Email);

/// <summary>PR-11 personel görünürlüğü: grubun halka açık sitede yayında olup olmadığı ve
/// değilse eksiklerin TAMAMI ("Foto yok" + "Tarife yok" birlikte gösterilir).</summary>
/// <param name="Adet">Vitrinde görünecek toplam (Σ VitrinAdet ?? 1) — <paramref name="AracSayisi"/>'ndan farklı olabilir.</param>
/// <param name="KarisikMod">Grupta hem çoklu kayıt hem VitrinAdet&gt;1 var — toplam beklenenden büyük olabilir.</param>
public sealed record GrupYayinDurumu(
    Guid GroupId, string Ad, bool Yayinda, IReadOnlyList<string> Eksikler,
    int AracSayisi, int Adet, bool KarisikMod);

/// <summary>
/// Public-site filo vitrini (PR-4). Yetki gerektirmez — mevcut guard-free okuma servisleri (
/// <see cref="VehicleGroupService.ListActiveAsync"/>, <see cref="VehicleService.ListAsync"/>,
/// <see cref="VehiclePhotoService"/>) üstünden salt-okur derleme (DashboardService deseni).
/// Vitrin GRUP bazlı (tekil araç/plaka değil — VehicleGroup.WebSira zaten bunun için tanımlanmış
/// ama hiç okunmuyordu); Vehicle.Grup FK değil string eşleşmesi (VehicleGroup.cs doc-yorumu).
/// </summary>
public sealed class FleetShowcaseService(
    VehicleGroupService groups, VehicleService vehicles, VehiclePhotoService photos,
    IPublicBrandingRepository branding, ITenantContext tenant,
    AvailabilityService availability, RentalQuoteEngine quotes, KdvVarsayilan kdv)
{
    public async Task<IReadOnlyList<FleetShowcaseCard>> ListShowcaseGroupsAsync(CancellationToken ct = default)
    {
        var cards = new List<FleetShowcaseCard>();
        foreach (var y in await YayindakiGruplarAsync(ct))
        {
            var g = y.Group;
            var meta = await photos.ListMetaAsync(y.KapakAraci.Id, ct); // zaten Sira sıralı; kapak aracında foto VAR
            cards.Add(new FleetShowcaseCard(g.Id, g.Ad, g.Aciklama, g.KasaTuru, g.KoltukSayisi, g.KapiSayisi, g.BagajSayisi,
                meta.Count > 0 ? meta[0].Id : null, Adet(y.Araclar)));
        }
        return cards;
    }

    /// <summary>PR-11: yayınlanmamış grup <b>404</b> döner (null). Sebep: eski bir sitemap girdisi ya
    /// da paylaşılmış link, fotosuz/fiyatsız bir grubu ziyaretçiye açmamalı — vitrin ile detayın
    /// yayın kararı TEK yerden gelir.</summary>
    public async Task<FleetShowcaseDetail?> GetGroupDetailAsync(Guid groupId, CancellationToken ct = default)
    {
        var y = (await YayindakiGruplarAsync(ct)).FirstOrDefault(x => x.Group.Id == groupId);
        if (y is null) return null;

        var photoIds = new List<Guid>();
        foreach (var v in y.Araclar)
            photoIds.AddRange((await photos.ListMetaAsync(v.Id, ct)).Select(m => m.Id));
        var group = y.Group;
        return new FleetShowcaseDetail(group.Id, group.Ad, group.Aciklama, group.KasaTuru,
            group.KoltukSayisi, group.KapiSayisi, group.BagajSayisi, photoIds, Adet(y.Araclar));
    }

    /// <summary>
    /// PR-11 — personel hazırlık ("pending") paneli: her aktif grubun yayına girip girmediği ve
    /// GİRMEDİYSE eksiklerin TAMAMI. Tek bir sebep göstermek yetmez: personel fotoyu yükler, kart
    /// yine çıkmaz, panel "yayında değil" der ve nedenini söylemez.
    ///
    /// Kapı ile AYNI toplu yardımcıları kullanır — panelin "tarife var" dediği yerde vitrinin
    /// elemesi (ör. wildcard tarife ya da tüm kademeleri 0 olan satır) mümkün olmamalı.
    /// </summary>
    public async Task<IReadOnlyList<GrupYayinDurumu>> ListYayinDurumuAsync(CancellationToken ct = default)
    {
        var adaylar = await GetAdaylarAsync(ct);
        var tumGruplar = (await groups.ListActiveAsync(ct)).OrderBy(g => g.WebSira).ToList();

        var fotolu = await photos.ListVehicleIdsWithPhotoAsync(
            [.. adaylar.SelectMany(a => a.Araclar).Select(v => v.Id).Distinct()], ct);
        var fiyatli = await quotes.FiyatlanabilirGruplarAsync(
            [.. tumGruplar.Select(Kod)], DateTimeOffset.UtcNow, ct: ct);

        var sonuc = new List<GrupYayinDurumu>();
        foreach (var g in tumGruplar)
        {
            // Araçsız grup da listelenir — "hiç araç yok" da bir eksiktir, sessizce kaybolmamalı.
            var araclar = adaylar.FirstOrDefault(a => a.Group.Id == g.Id).Araclar ?? [];
            var eksikler = new List<string>();
            if (araclar.Count == 0) eksikler.Add("Uygun araç yok");
            else if (!araclar.Any(v => fotolu.Contains(v.Id))) eksikler.Add("Foto yok");
            if (!fiyatli.Contains(Kod(g))) eksikler.Add("Tarife yok");

            // Karışım kayması: hem VitrinAdet dolu kayıt hem birden fazla kayıt varsa toplam
            // beklenenden büyük olabilir (ör. "12 adet"lik kayıt + 3 tekil kayıt = 15).
            var karisikMod = araclar.Count > 1 && araclar.Any(v => v.VitrinAdet is > 1);

            sonuc.Add(new GrupYayinDurumu(g.Id, g.Ad, eksikler.Count == 0, eksikler, araclar.Count,
                Adet(araclar), karisikMod));
        }
        return sonuc;
    }

    public Task<FleetBranding> GetBrandingAsync(CancellationToken ct = default)
        => branding.GetAsync(tenant.TenantIdOrThrow(), ct);

    /// <summary>PR-9: SEO kanonik host'u (canonical link + sitemap + robots TEK kaynağı).</summary>
    public Task<string?> GetCanonicalHostAsync(CancellationToken ct = default)
        => branding.GetCanonicalHostAsync(tenant.TenantIdOrThrow(), ct);

    /// <summary>
    /// PR-7: anonim müsaitlik+fiyat araması. `MusaitlikArama.razor`'ın (iç ekran) deseniyle BİREBİR aynı:
    /// <see cref="AvailabilityService.FindAvailableAsync"/> (guard-free) → uygun grup → grup başına
    /// <see cref="RentalQuoteEngine.QuoteAsync"/> (guard-free), `ValidationException` GRUP BAZINDA yutulur
    /// (geçersiz kampanya/kural bir kartı düşürür, sayfa çökmez). `KiraHesapService` KULLANILAMAZ —
    /// `Permission.OperationsWrite` ister.
    ///
    /// Grup→araç eşleşmesi PR-4.5'in `GetEligibleCandidatesByGroupAsync` helper'ı (Türkçe-duyarlı).
    /// Fiyatı olmayan grup (tarife matrisi yok → `GunlukUcret = 0`) sonuçta GÖSTERİLMEZ: staff "—" görebilir
    /// ama ziyaretçiye fiyatsız kart kafa karıştırıcıdır.
    /// </summary>
    public async Task<IReadOnlyList<PublicAvailabilityResult>> SearchAvailabilityAsync(
        DateTimeOffset from, DateTimeOffset to, string? sube, CancellationToken ct = default)
    {
        var musait = (await availability.FindAvailableAsync(from, to, null, sube, ct))
            .Where(v => !v.WebRezKapat).ToList();
        if (musait.Count == 0) return [];

        var oran = await kdv.OranAsync(ct);
        var results = new List<PublicAvailabilityResult>();

        foreach (var y in await YayindakiGruplarAsync(ct))
        {
            var g = y.Group;
            // Grubun bu tarih aralığında GERÇEKTEN müsait araçları (vitrin adayı ≠ müsait araç).
            // PR-11: adet buradan sayılır — biri kirada ise ziyaretçi 12 değil 11 görür.
            var musaitOlanlar = musait.Where(v => TurkishText.EqualsIgnoreTurkishCase(v.Grup, g.Ad)).ToList();
            if (musaitOlanlar.Count == 0) continue;

            QuoteResult quote;
            try
            {
                quote = await quotes.QuoteAsync(new QuoteRequest
                { AracGrupKod = g.Kod, BasTar = from, BitTar = to, Sube = sube, SigortaUrunKodlari = [] }, ct);
            }
            catch (ValidationException) { continue; } // MusaitlikArama'daki AYNI yutma deseni
            if (quote.GunlukUcret <= 0m) continue;

            var meta = await photos.ListMetaAsync(y.KapakAraci.Id, ct);
            results.Add(new PublicAvailabilityResult(
                g.Id, g.Ad, g.Aciklama, g.KasaTuru, g.KoltukSayisi, g.KapiSayisi, g.BagajSayisi,
                meta.Count > 0 ? meta[0].Id : null,
                g.Kod, quote.Gun, quote.ParaBirimi,
                quote.GunlukUcret, Brut(quote.GunlukUcret, oran),
                quote.GenelToplam, Brut(quote.GenelToplam, oran), Adet(musaitOlanlar)));
        }

        return results.OrderBy(r => r.GunlukUcretKdvDahil).ToList();
    }

    /// <summary>NET → BRÜT (yalnız gösterim). Kuruşa yuvarlanır; motor zaten 2 ondalık döndürür.</summary>
    private static decimal Brut(decimal net, decimal oran) => Math.Round(net * (1m + oran), 2, MidpointRounding.AwayFromZero);

    /// <summary>PR-4.5: aktif grup → o gruba uygun (WebRezKapat=false) araç eşleşmesi —
    /// `Vehicle.Grup` serbest metin olduğu için `TurkishText.EqualsIgnoreTurkishCase` ile eşleştirilir
    /// (ordinal/`OrdinalIgnoreCase` DEĞİL — İ/I/ı'da sessizce kaçırır, bkz. TurkishText doc-yorumu).
    /// Aynı `Ad`'a sahip birden fazla aktif grup varsa HER İKİSİ de (WebSira sıralı) bağımsız değerlendirilir
    /// — `ToDictionary(g => g.Ad)` KULLANILMAZ (tekil olmayan anahtarda çöker).
    /// PR-11: artık TEMSİLCİ değil, gruba ait TÜM uygun araçlar döner (kapak seçimi, adet toplamı ve
    /// foto kapısı hepsi tam listeye ihtiyaç duyar).</summary>
    private async Task<List<(VehicleGroup Group, List<Vehicle> Araclar)>> GetAdaylarAsync(CancellationToken ct)
    {
        var activeGroups = (await groups.ListActiveAsync(ct)).OrderBy(g => g.WebSira).ToList();
        var eligibleVehicles = (await vehicles.ListAsync(ct)).Where(v => !v.WebRezKapat).ToList();

        var result = new List<(VehicleGroup, List<Vehicle>)>();
        foreach (var g in activeGroups)
        {
            var esleşen = eligibleVehicles.Where(v => TurkishText.EqualsIgnoreTurkishCase(v.Grup, g.Ad)).ToList();
            if (esleşen.Count > 0) result.Add((g, esleşen));
        }
        return result;
    }

    /// <summary>PR-11 yayınlanmış grup: kapak fotosu OLAN bir aracı ve geçerli tarifesi var.</summary>
    private sealed record YayindakiGrup(VehicleGroup Group, List<Vehicle> Araclar, Vehicle KapakAraci);

    /// <summary>
    /// PR-11 — <b>YAYIN KAPISI</b>. Bir grup halka açık sitede ancak (a) uygun araçlarından en az
    /// birinin FOTOĞRAFI ve (b) geçerli bir TARİFESİ varsa görünür. Araçlar bu iki şart sağlanana
    /// kadar "pending" bekler; personel foto yükleyip fiyat girdiğinde grup kendiliğinden yayına girer.
    ///
    /// <para><b>Tek kaynak:</b> vitrin, arama, <c>/araclar/{id}</c> detayı ve <c>sitemap.xml</c>'in
    /// dördü de buradan beslenir. Ayrı ayrı yazılsalardı biri yayınlar diğeri 404 verirdi.</para>
    ///
    /// <para><b>İki toplu sorgu:</b> tüm adayların fotoğraf varlığı TEK sorguda
    /// (<see cref="VehiclePhotoService.ListVehicleIdsWithPhotoAsync"/>), tüm grupların fiyatlanabilirliği
    /// TEK sorguda (<see cref="RentalQuoteEngine.FiyatlanabilirGruplarAsync"/>). Grup/araç başına
    /// çağrı, rate-limit'siz en sıcak anonim sayfada N+1 üretirdi.</para>
    ///
    /// <para><b>KAPI ⊇ ARAMA:</b> kapı aramanın kabul ettiği her durumu kabul eder (tarife penceresi
    /// geniş, şube/kanal belirtilmemiş). Aksi halde arama kart basar, kartın "Detay" linki 404 verir.</para>
    /// </summary>
    private async Task<List<YayindakiGrup>> YayindakiGruplarAsync(CancellationToken ct)
    {
        var adaylar = await GetAdaylarAsync(ct);
        if (adaylar.Count == 0) return [];

        var fotolu = await photos.ListVehicleIdsWithPhotoAsync(
            [.. adaylar.SelectMany(a => a.Araclar).Select(v => v.Id).Distinct()], ct);
        var fiyatli = await quotes.FiyatlanabilirGruplarAsync(
            [.. adaylar.Select(a => Kod(a.Group))], DateTimeOffset.UtcNow, ct: ct);

        var sonuc = new List<YayindakiGrup>();
        foreach (var (g, araclar) in adaylar)
        {
            // Kapak: fotosu OLAN ilk araç. Eskiden tek temsilci alınıyordu ve onda foto yoksa kart
            // fotosuz kalıyordu — grubun başka aracında foto olsa bile.
            var kapak = araclar.FirstOrDefault(v => fotolu.Contains(v.Id));
            if (kapak is null) continue;                 // foto şartı
            if (!fiyatli.Contains(Kod(g))) continue;     // fiyat şartı
            sonuc.Add(new YayindakiGrup(g, araclar, kapak));
        }
        return sonuc;
    }

    /// <summary>Grup kodunu motorun <c>QuoteAsync</c>'iyle AYNI şekilde normalize eder (trim+upper) —
    /// kapı ile motor farklı normalize etseydi eşleşme sessizce kaçardı.</summary>
    private static string Kod(VehicleGroup g) => (g.Kod ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>PR-11 vitrin adedi: <c>Σ (VitrinAdet ?? 1)</c>. Tek formül üç modeli de karşılar —
    /// 12 ayrı kayıt (12×1), tek kayıt "12 adet" (1×12), karışık. YALNIZ gösterim.</summary>
    private static int Adet(IEnumerable<Vehicle> araclar) => araclar.Sum(v => v.VitrinAdet ?? 1);
}
