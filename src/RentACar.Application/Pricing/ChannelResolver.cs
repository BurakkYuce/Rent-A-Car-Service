using RentACar.Domain.Entities;

namespace RentACar.Application.Pricing;

/// <summary>
/// FAZ 3.A4 çiti — rezervasyon KAYNAĞI metnini doğrulanmış tarife KANALI'na çevirir.
/// Kural: boş → null; yalnız AKTİF <see cref="ReservationSource"/> listesinde Kod VEYA Ad ile
/// (Trim + case-insensitive) eşleşirse Trim'li metin döner; eşleşmezse null — tanımsız/pasif kaynak
/// kanal-özel tarife SEÇTİREMEZ (sessiz yanlış-tarife yerine base matris).
///
/// <para>FAZ-48: kural buraya alındı çünkü müsaitlik arama ekranı da kaynak seçtirip fiyat motoruna
/// kanal geçiriyor; <c>PricingService</c> buna delege eder — iki kopya sapmasın.</para>
/// </summary>
public static class ChannelResolver
{
    public static string? Resolve(string? source, IReadOnlyList<ReservationSource> activeSources)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var k = source.Trim();
        return activeSources.Any(s =>
            string.Equals(s.Kod, k, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Ad, k, StringComparison.OrdinalIgnoreCase)) ? k : null;
    }
}
