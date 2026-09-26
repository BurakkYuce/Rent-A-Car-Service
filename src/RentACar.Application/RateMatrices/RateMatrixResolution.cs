using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.RateMatrices;

/// <summary>Tarife matrisi çözümleme sorgusu: hangi kanal/şube/grup/tarih/kaç-gün için fiyat.</summary>
public sealed record RateMatrisSorgu(
    string? Kanal, string? Sube, string? AracGrupKod, DateTimeOffset Tarih, int GunSayisi, string? ParaBirimi = null);

/// <summary>Çözümleme sonucu: eşleşen matris satırı + kademe günlük fiyatı + toplam.</summary>
public sealed record RateMatrisSonuc(
    Guid Id, string Kod, string Ad, decimal GunlukFiyat, string? ParaBirimi, decimal ToplamFiyat);

/// <summary>
/// TEST-ORACLE — üretim (booking) çözümleyicisi <c>RentalQuoteEngine.SelectMatrix</c>; bu sınıf
/// bağımsız-oracle doğrulaması için tutulur (RateMatrisCozumlemeTests). Ek olarak tarife-matris
/// ekranının fiyat-sorgu paneli (RateMatrixList → RateMatrixService.CozumleAsync) bunu kullanır;
/// booking/kira fiyat akışına BAĞLI DEĞİLDİR.
///
/// Tarife matrisi ÇÖZÜMLEME (fiyat bulma) — SAF + deterministik (bağımsız oracle ile birim-testlenir).
/// Verilen kanal×şube×araç-grup×tarih×gün-sayısı için Onaylı+Aktif+kapsam-uyumlu+tarih-geçerli ve o
/// gün-kademesini (1..7, 7+ → Gün7) FİYATLAYAN satırlar arasından deterministik seçer:
/// (1) en spesifik (en az joker/null-kapsam), (2) en dar geçerlilik penceresi, (3) en yeni,
/// (4) Kod. Joker: satırın kapsam alanı null ise her sorguya uyar; doluysa sorgu değeriyle
/// (case-insensitive) eşleşmeli. Deftere DOKUNMAZ (saf fiyat okuma).
/// </summary>
public static class RateMatrixResolution
{
    public static RateMatrisSonuc? Resolve(IEnumerable<RateMatrix> rows, RateMatrisSorgu q)
    {
        if (q.GunSayisi < 1) return null;
        var tier = Math.Clamp(q.GunSayisi, 1, 7);

        var candidates = rows
            .Where(r => r.Aktif && r.OnayDurumu == TariffApprovalStatus.Onayli)
            .Where(r => Scope(r.Kanal, q.Kanal) && Scope(r.Sube, q.Sube) && Scope(r.AracGrupKod, q.AracGrupKod))
            .Where(r => Scope(r.ParaBirimi, q.ParaBirimi)) // para birimi de joker/scope semantiği (adversarial Medium):
                                                            // satır-döviz doluysa sorgu eşleşmeli; sorgu para vermezse
                                                            // YALNIZ varsayılan (null-döviz) satır → yabancı-döviz sızmaz
            .Where(r => IsDateEligible(r, q.Tarih))
            .Where(r => DayPrice(r, tier) is not null)   // yalnız bu kademeyi fiyatlayan satırlar
            .ToList();
        if (candidates.Count == 0) return null;

        var winner = candidates
            .OrderByDescending(Specificity)                 // en spesifik (en az joker) önce
            .ThenBy(WindowWidth)                     // en dar geçerlilik penceresi
            .ThenByDescending(r => r.CreatedAtUtc)        // en yeni
            .ThenBy(r => r.Kod, StringComparer.Ordinal)  // son deterministik kırıcı
            .First();

        var daily = DayPrice(winner, tier)!.Value;
        return new RateMatrisSonuc(winner.Id, winner.Kod, winner.Ad, daily, winner.ParaBirimi, daily * q.GunSayisi);
    }

    // Kapsam (joker) eşleşmesi: satır null → her sorguya uyar; doluysa sorgu değeriyle case-insensitive eşleşir.
    private static bool Scope(string? rowVal, string? qVal)
        => rowVal is null || (qVal is not null && string.Equals(rowVal.Trim(), qVal.Trim(), StringComparison.OrdinalIgnoreCase));

    // Geçerlilik: BasTar dahil, BitTar dahil; null → açık uç.
    private static bool IsDateEligible(RateMatrix r, DateTimeOffset t)
        => (r.BasTar is null || t >= r.BasTar) && (r.BitTar is null || t <= r.BitTar);

    private static int Specificity(RateMatrix r)
        => (r.Kanal is not null ? 1 : 0) + (r.Sube is not null ? 1 : 0)
         + (r.AracGrupKod is not null ? 1 : 0) + (r.ParaBirimi is not null ? 1 : 0);

    private static double WindowWidth(RateMatrix r)
        => r is { BasTar: { } b, BitTar: { } t } ? (t - b).TotalDays : double.PositiveInfinity;

    private static decimal? DayPrice(RateMatrix r, int tier) => tier switch
    {
        1 => r.Gun1, 2 => r.Gun2, 3 => r.Gun3, 4 => r.Gun4, 5 => r.Gun5, 6 => r.Gun6, _ => r.Gun7
    };
}
