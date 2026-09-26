using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>
/// Vade panosu: sigorta/MTV/muayene bitiş tarihlerini birleştirip kova + kalan günle
/// sınıflar. Dashboard uyarılarını ve /vade sayfasını besler.
/// </summary>
public sealed class DueService(IRegulationRepository repository)
{
    private readonly IRegulationRepository _repository = repository;

    /// <summary>Tüm vade kalemleri, en yakın bitişe göre sıralı.</summary>
    public async Task<IReadOnlyList<VadeItem>> GetAllAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var sources = await _repository.GetDueSourcesAsync(ct);
        return sources
            .Select(s =>
            {
                var (remaining, bucket) = DueCalculation.Classify(reference, s.Bitis);
                return new VadeItem(s.VehicleId, s.Tur, s.Bitis, remaining, bucket);
            })
            .OrderBy(i => i.Bitis)
            .ToList();
    }

    /// <summary>Yalnız uyarı gerektirenler (geçmiş / ≤30 gün).</summary>
    public async Task<IReadOnlyList<VadeItem>> GetWarningsAsync(DateTimeOffset? now = null, CancellationToken ct = default)
        => (await GetAllAsync(now, ct)).Where(i => i.Bucket != DueBucket.Ileri).ToList();

    /// <summary>FAZ 2.1: tek aracın vade kalemleri (karne "Yaklaşan Vadeler" bloğu). Union'a
    /// dokunulmaz (bildirim üreticiyle paylaşımlı — O12a); saf filtre.</summary>
    public async Task<IReadOnlyList<VadeItem>> GetForVehicleAsync(
        Guid vehicleId, DateTimeOffset? now = null, CancellationToken ct = default)
        => (await GetAllAsync(now, ct)).Where(i => i.VehicleId == vehicleId).ToList();

    /// <summary>FAZ 2.1: araç-başına UYARI sayısı (geçmiş + ≤30 gün) — filo panosu "Vade" kolonu.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetWarningCountsByVehicleAsync(
        DateTimeOffset? now = null, CancellationToken ct = default)
        => (await GetWarningsAsync(now, ct))
            .GroupBy(i => i.VehicleId)
            .ToDictionary(g => g.Key, g => g.Count());
}
