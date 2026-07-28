using RentACar.Application.Availability;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Pricing;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Fleet;

public sealed record FleetShowcaseCard(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, Guid? CoverPhotoId);

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
    decimal ToplamKdvHaric, decimal ToplamKdvDahil);

public sealed record FleetShowcaseDetail(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, IReadOnlyList<Guid> PhotoIds);

public sealed record FleetBranding(string? Marka, string? Adres, string? Tel, string? Email);

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
        foreach (var (g, candidate) in await GetEligibleCandidatesByGroupAsync(ct))
        {
            var meta = await photos.ListMetaAsync(candidate.Id, ct); // zaten Sira sıralı
            cards.Add(new FleetShowcaseCard(g.Id, g.Ad, g.Aciklama, g.KasaTuru, g.KoltukSayisi, g.KapiSayisi, g.BagajSayisi,
                meta.Count > 0 ? meta[0].Id : null));
        }
        return cards;
    }

    public async Task<FleetShowcaseDetail?> GetGroupDetailAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = (await groups.ListActiveAsync(ct)).FirstOrDefault(g => g.Id == groupId);
        if (group is null) return null;
        var candidates = (await vehicles.ListAsync(ct))
            .Where(v => !v.WebRezKapat && TurkishText.EqualsIgnoreTurkishCase(v.Grup, group.Ad))
            .ToList();

        var photoIds = new List<Guid>();
        foreach (var v in candidates)
            photoIds.AddRange((await photos.ListMetaAsync(v.Id, ct)).Select(m => m.Id));
        return new FleetShowcaseDetail(group.Id, group.Ad, group.Aciklama, group.KasaTuru,
            group.KoltukSayisi, group.KapiSayisi, group.BagajSayisi, photoIds);
    }

    public Task<FleetBranding> GetBrandingAsync(CancellationToken ct = default)
        => branding.GetAsync(tenant.TenantIdOrThrow(), ct);

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

        foreach (var (g, candidate) in await GetEligibleCandidatesByGroupAsync(ct))
        {
            // Grubun bu tarih aralığında GERÇEKTEN müsait aracı var mı? (vitrin adayı ≠ müsait araç)
            if (!musait.Any(v => TurkishText.EqualsIgnoreTurkishCase(v.Grup, g.Ad))) continue;

            QuoteResult quote;
            try
            {
                quote = await quotes.QuoteAsync(new QuoteRequest
                { AracGrupKod = g.Kod, BasTar = from, BitTar = to, Sube = sube, SigortaUrunKodlari = [] }, ct);
            }
            catch (ValidationException) { continue; } // MusaitlikArama'daki AYNI yutma deseni
            if (quote.GunlukUcret <= 0m) continue;

            var meta = await photos.ListMetaAsync(candidate.Id, ct);
            results.Add(new PublicAvailabilityResult(
                g.Id, g.Ad, g.Aciklama, g.KasaTuru, g.KoltukSayisi, g.KapiSayisi, g.BagajSayisi,
                meta.Count > 0 ? meta[0].Id : null,
                g.Kod, quote.Gun, quote.ParaBirimi,
                quote.GunlukUcret, Brut(quote.GunlukUcret, oran),
                quote.GenelToplam, Brut(quote.GenelToplam, oran)));
        }

        return results.OrderBy(r => r.GunlukUcretKdvDahil).ToList();
    }

    /// <summary>NET → BRÜT (yalnız gösterim). Kuruşa yuvarlanır; motor zaten 2 ondalık döndürür.</summary>
    private static decimal Brut(decimal net, decimal oran) => Math.Round(net * (1m + oran), 2, MidpointRounding.AwayFromZero);

    /// <summary>PR-4.5: aktif grup → o gruba uygun (WebRezKapat=false) TEMSİLCİ araç eşleşmesi —
    /// `Vehicle.Grup` serbest metin olduğu için `TurkishText.EqualsIgnoreTurkishCase` ile eşleştirilir
    /// (ordinal/`OrdinalIgnoreCase` DEĞİL — İ/I/ı'da sessizce kaçırır, bkz. TurkishText doc-yorumu).
    /// Aynı `Ad`'a sahip birden fazla aktif grup varsa HER İKİSİ de (WebSira sıralı) bağımsız değerlendirilir
    /// — `ToDictionary(g => g.Ad)` KULLANILMAZ (tekil olmayan anahtarda çöker); roadmap PR-7'nin
    /// `SearchAvailabilityAsync`'i de AYNI helper'ı kullanır.</summary>
    private async Task<IReadOnlyList<(VehicleGroup Group, Vehicle Candidate)>> GetEligibleCandidatesByGroupAsync(CancellationToken ct)
    {
        var activeGroups = (await groups.ListActiveAsync(ct)).OrderBy(g => g.WebSira).ToList();
        var eligibleVehicles = (await vehicles.ListAsync(ct)).Where(v => !v.WebRezKapat).ToList();

        var result = new List<(VehicleGroup, Vehicle)>();
        foreach (var g in activeGroups)
        {
            var candidate = eligibleVehicles.FirstOrDefault(v => TurkishText.EqualsIgnoreTurkishCase(v.Grup, g.Ad));
            if (candidate is not null) result.Add((g, candidate));
        }
        return result;
    }
}
