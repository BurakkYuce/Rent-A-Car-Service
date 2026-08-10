using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// FAZ-49 — rezervasyon/kira belgesindeki SERBEST METİN <c>Kaynak</c> alanını kural matrisi taşıyan
/// <see cref="ReservationSource"/> kaydına çözer. Saf kural gövdesi <see cref="RezKaynakKural"/>'da;
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
public sealed class RezKaynakKuralService(ReservationSourceService kaynaklar)
{
    private readonly ReservationSourceService _kaynaklar = kaynaklar;

    /// <summary>Serbest metin kaynak → kural kaydı; boş/tanımsız metin için null (kural yok).</summary>
    public async Task<ReservationSource?> CozAsync(string? kaynak, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kaynak)) return null;
        var k = kaynak.Trim();
        var hepsi = await _kaynaklar.ListAsync(ct);

        return Sec(hepsi.Where(s => string.Equals(s.Kod, k, StringComparison.OrdinalIgnoreCase)))
            ?? Sec(hepsi.Where(s => string.Equals(s.Ad, k, StringComparison.OrdinalIgnoreCase)));

        static ReservationSource? Sec(IEnumerable<ReservationSource> adaylar)
            => adaylar.OrderByDescending(s => s.Aktif).ThenBy(s => s.Kod, StringComparer.Ordinal).FirstOrDefault();
    }
}
