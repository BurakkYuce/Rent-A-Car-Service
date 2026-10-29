using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

/// <summary>
/// Olası mükerrer kararı. Geçişler plaka + tutar başına ana göre sıralı tutulur (iki kural da aynı tutarı şart koşar;
/// 20.000 satırlık dosyada her satırı her satırla karşılaştırmak yerine yalnız aynı plaka + tutar grubu taranır).
/// <list type="bullet">
/// <item><b>60 sn kuralı</b> (#391 M1): aynı plaka + tutar + |Δt| &lt; 60 sn; kümede yansıtılmış ya da öncelik sırasında
/// önce gelen bir satır varsa ikincil.</item>
/// <item><b>Geniş kural</b> (#391 R2 L1, #407 M1): <see cref="HgsResolution.AreWideCandidates"/> çiftleri arasında BİRE BİR
/// eşleşme. Satırlar öncelik sırasıyla işlenir; her satır kendinden önce gelen adaylardan zamanca en yakınına eşlenir
/// ve ikincil olur — ancak o aday bu satırın dosyasından başka bir satırla zaten eşlenmemişse (bir satır her diğer
/// dosyada en çok bir satırla eşleşir; zincirleme yok). Yeni içe aktarımlar öncelikte daima sonra geldiğinden önceki
/// kararlar yeni dosyayla değişmez.</item>
/// <item>Elle onaylanmış satır (<see cref="HgsCrossingRef.Approved"/>) iki kuraldan da muaftır ve kimseyi ikincil
/// yapmaz.</item>
/// </list>
/// Sorgu aralığı <see cref="HgsResolution.LookAround"/> (±12 sa) olmalıdır.
/// </summary>
public sealed class HgsNearDuplicateIndex
{
    private readonly Dictionary<(string Plate, decimal Amount), List<HgsCrossingRef>> _groups = [];
    private Dictionary<Guid, (HgsCrossingRef Partner, bool Secondary)>? _pairs;

    public HgsNearDuplicateIndex(IEnumerable<HgsCrossingRef> refs)
    {
        foreach (var r in refs) Add(r);
    }

    public void Add(HgsCrossingRef r)
    {
        var key = (r.Plate, decimal.Round(r.Amount, 2));
        if (!_groups.TryGetValue(key, out var list)) _groups[key] = list = [];
        list.Insert(LowerBound(list, r.At), r);
        _pairs = null;
    }

    /// <summary>
    /// <paramref name="c"/> ikincil mi (otomatik borçlandırılmaz)? <paramref name="reflected"/>: bu işlemde yansıtılan
    /// satırlar (yansıtma kilidi altında).
    /// </summary>
    public bool IsSecondary(HgsCrossingRef c, Func<HgsCrossingRef, bool>? reflected = null)
    {
        if (c.Approved) return false;
        bool Charged(HgsCrossingRef d) => d.Status == HgsCrossingStatus.Yansitildi || (reflected?.Invoke(d) ?? false);
        if (Within(c, HgsResolution.NearDuplicateWindow)
            .Any(d => !d.Approved && HgsResolution.AreNearDuplicates(c, d) && (Charged(d) || HgsResolution.Precedence(d, c) < 0)))
            return true;
        _pairs ??= BuildPairs();
        return _pairs.TryGetValue(c.Id, out var p) && (p.Secondary || Charged(p.Partner));
    }

    private Dictionary<Guid, (HgsCrossingRef, bool)> BuildPairs()
    {
        var pairs = new Dictionary<Guid, (HgsCrossingRef, bool)>();
        var taken = new HashSet<(Guid Primary, Guid Batch)>();
        foreach (var list in _groups.Values)
        {
            if (list.Count < 2) continue;
            foreach (var c in list.Order(HgsResolution.PrecedenceOrder))
            {
                HgsCrossingRef? best = null;
                foreach (var d in Within(c, HgsResolution.SameStationWindow))
                {
                    if (HgsResolution.Precedence(d, c) >= 0 || !HgsResolution.AreWideCandidates(c, d)
                        || taken.Contains((d.Id, c.Batch))) continue;
                    if (best is null || Closer(c, d, best)) best = d;
                }
                if (best is null) continue;
                taken.Add((best.Id, c.Batch));
                pairs[c.Id] = (best, true);
                pairs.TryAdd(best.Id, (c, false)); // asıl: ilk eşi (yansıtılmışsa asıl borçlanmaz — eski veri koruması)
            }
        }
        return pairs;
    }

    private static bool Closer(HgsCrossingRef c, HgsCrossingRef d, HgsCrossingRef best)
    {
        var x = (d.At - c.At).Duration().CompareTo((best.At - c.At).Duration());
        return x < 0 || (x == 0 && HgsResolution.Precedence(d, best) < 0);
    }

    /// <summary>Aynı plaka + tutar grubunda |Δt| &lt; <paramref name="window"/> olan diğer satırlar.</summary>
    private IEnumerable<HgsCrossingRef> Within(HgsCrossingRef c, TimeSpan window)
    {
        if (!_groups.TryGetValue((c.Plate, decimal.Round(c.Amount, 2)), out var list)) yield break;
        for (var i = LowerBound(list, c.At - window); i < list.Count; i++)
        {
            var d = list[i];
            if (d.At - c.At >= window) yield break;
            if (d.Id != c.Id && (d.At - c.At).Duration() < window) yield return d;
        }
    }

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
