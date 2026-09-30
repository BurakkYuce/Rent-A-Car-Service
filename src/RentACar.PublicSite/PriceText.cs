using System.Globalization;

namespace RentACar.PublicSite;

/// <summary>
/// Sitede basılan TEK fiyat biçimi. Eskiden üç ayrı biçim vardı ("1.500 ₺", "₺1.500,00", "N0") ve
/// hepsi sunucunun ORTAM kültürüne bağlıydı: Linux konteynerde invariant kültür gelirse "1,500" basılır,
/// Türk okuyucu bunu bir buçuk lira okur. Kültür burada tr-TR'ye SABİTLENİR (llms.txt ile aynı kural).
/// </summary>
public static class PriceText
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"₺1.250" — kuruş varsa "₺1.250,50". Fiyatlar TRY tabanlı (ilan kuralı).</summary>
    public static string Tl(decimal amount)
        => decimal.Truncate(amount) == amount
            ? "₺" + amount.ToString("N0", Tr)
            : "₺" + amount.ToString("N2", Tr);

    /// <summary>Tamsayı (gün, adet) — binlik ayraçlı, tr-TR.</summary>
    public static string Count(int value) => value.ToString("N0", Tr);

    /// <summary>Tarih "12 Ekim 2026" — ay adı Türkçe, ortam kültüründen bağımsız.</summary>
    public static string Date(DateTimeOffset value) => value.ToString("d MMMM yyyy", Tr);

    /// <summary>Kısa tarih "12 Eki" (aralık özetinde).</summary>
    public static string ShortDate(DateTimeOffset value) => value.ToString("d MMM", Tr);
}
