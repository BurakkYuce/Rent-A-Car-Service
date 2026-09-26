using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// FAZ-49 — rezervasyon/kira belgesindeki SERBEST METİN <c>Kaynak</c> alanını kural matrisi taşıyan
/// <see cref="ReservationSource"/> kaydına çözer. Saf kural gövdesi <see cref="ReservationSourceRule"/>'da;
/// burada yalnız arama/çözümleme var.
///
/// <para><b>Neden AKTİF olmayanlar da aranır (PricingService.KanalCoz'dan farkı):</b> orada amaç
/// tarife SEÇİMİdir — tanımsız/pasif kaynak kanal-özel tarife seçtirmemelidir. Burada amaç KURAL
/// uygulamaktır: kaynağı pasife çekmek, o kaynağın sözleşme koşulunu (ör. "uzatılamaz") ortadan
/// kaldırmamalıdır — aksi halde bayrağı aşmak için kaynağı bir tık pasife almak yeterdi.</para>
///
/// <para><b>Determinizm:</b> önce Kod, sonra Ad eşleşmesi (Trim + case-insensitive); eşit adaylarda
/// AKTİF olan, sonra Kod sırası kazanır — aynı metin iki kayda uysa bile sonuç sabittir.</para>
/// </summary>
public sealed class ReservationSourceRuleService(ReservationSourceService sources)
{
    private readonly ReservationSourceService _sources = sources;

    /// <summary>Serbest metin kaynak → kural kaydı; boş/tanımsız metin için null (kural yok).</summary>
    public async Task<ReservationSource?> ResolveAsync(string? source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var k = source.Trim();
        var all = await _sources.ListAsync(ct);

        return Select(all.Where(s => string.Equals(s.Kod, k, StringComparison.OrdinalIgnoreCase)))
            ?? Select(all.Where(s => string.Equals(s.Ad, k, StringComparison.OrdinalIgnoreCase)));

        static ReservationSource? Select(IEnumerable<ReservationSource> candidates)
            => candidates.OrderByDescending(s => s.Aktif).ThenBy(s => s.Kod, StringComparer.Ordinal).FirstOrDefault();
    }
}
