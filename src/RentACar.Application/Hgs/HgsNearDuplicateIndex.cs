using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

/// <summary>
/// Olası mükerrer sorgusu için plaka başına ana göre sıralı dizin: bir geçişin ±<see cref="HgsResolution.LookAround"/> komşuları ikili aramayla bulunur
/// (20.000 satırlık dosyada her satırı her satırla karşılaştırmak yerine). Kural <see cref="HgsResolution"/>'dadır;
/// bu sınıf yalnız hızlandırır.
/// </summary>
public sealed class HgsNearDuplicateIndex
{
    private readonly Dictionary<string, List<HgsCrossingRef>> _byPlate = new(StringComparer.Ordinal);

    public HgsNearDuplicateIndex(IEnumerable<HgsCrossingRef> refs)
    {
        foreach (var r in refs) Add(r);
    }

    public void Add(HgsCrossingRef r)
    {
        if (!_byPlate.TryGetValue(r.Plate, out var list)) _byPlate[r.Plate] = list = [];
        list.Insert(LowerBound(list, r.At), r);
    }

    /// <summary><see cref="HgsResolution.AreNearDuplicates"/> ile komşu olan diğer geçişler.</summary>
    public IEnumerable<HgsCrossingRef> Near(HgsCrossingRef c)
    {
        if (!_byPlate.TryGetValue(c.Plate, out var list)) yield break;
        for (var i = LowerBound(list, c.At - HgsResolution.LookAround); i < list.Count; i++)
        {
            var d = list[i];
            if (d.At - c.At >= HgsResolution.LookAround) yield break;
            if (HgsResolution.AreNearDuplicates(c, d)) yield return d;
        }
    }

    /// <summary>
    /// <paramref name="c"/> kümenin ikincil üyesi mi: komşulardan biri yansıtılmışsa (ya da <paramref name="reflected"/>
    /// bu işlemde yansıtıldı diyorsa) veya öncelik sırasında önce geliyorsa evet.
    /// </summary>
    public bool IsSecondary(HgsCrossingRef c, Func<HgsCrossingRef, bool>? reflected = null)
        => Near(c).Any(d => d.Status == HgsCrossingStatus.Yansitildi
                            || (reflected?.Invoke(d) ?? false)
                            || HgsResolution.Precedence(d, c) < 0);

    private static int LowerBound(List<HgsCrossingRef> list, DateTimeOffset at)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (list[mid].At < at) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
