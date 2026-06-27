using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>
/// Vade panosu: sigorta/MTV/muayene bitiş tarihlerini birleştirip kova + kalan günle
/// sınıflar. Dashboard uyarılarını ve /vade sayfasını besler.
/// </summary>
public sealed class VadeService(IRegulationRepository repository)
{
    private readonly IRegulationRepository _repository = repository;

    /// <summary>Tüm vade kalemleri, en yakın bitişe göre sıralı.</summary>
    public async Task<IReadOnlyList<VadeItem>> GetAllAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var sources = await _repository.GetVadeSourcesAsync(ct);
        return sources
            .Select(s =>
            {
                var (kalan, bucket) = VadeHesap.Classify(reference, s.Bitis);
                return new VadeItem(s.VehicleId, s.Tur, s.Bitis, kalan, bucket);
            })
            .OrderBy(i => i.Bitis)
            .ToList();
    }

    /// <summary>Yalnız uyarı gerektirenler (geçmiş / ≤30 gün).</summary>
    public async Task<IReadOnlyList<VadeItem>> GetWarningsAsync(DateTimeOffset? now = null, CancellationToken ct = default)
        => (await GetAllAsync(now, ct)).Where(i => i.Bucket != VadeBucket.Ileri).ToList();
}
