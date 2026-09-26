namespace RentACar.Domain.Common;

/// <summary>
/// Yakıt seviyesi ölçeği — TEK iç ölçek (kullanıcı kararı 2026-09-25, DEVIR Karar (3)): <b>0–12</b> (depo on
/// ikide biri; formlar ve referans sistem TürevRent de böyle). Kira sözleşmesi, BAF (araç tahsis) ve servis
/// kaydı yakıt kolonları bu ölçekte saklanır; eksik yakıt bedeli = (çıkış − dönüş) × <c>YakitBirimUcret</c> →
/// birim ücret "on ikide bir depo başına"dır.
///
/// <para>Harici JWT API (<c>/api/v1/rentals</c>) yüzde (0–100) sözleşmesini KORUR; çeviri yalnız o sınırda bu
/// sınıfla yapılır. Çeviri en yakına yuvarlar; tamsayı girdide yarım (x.5) durumu matematiksel olarak oluşmaz
/// (12p ≡ 50 mod 100 çift/tek çelişkisi; 100t/12 = 25t/3 paydası 3) → yuvarlama yönü belirsizliği yok.</para>
/// </summary>
public static class FuelScale
{
    /// <summary>İç ölçeğin üst sınırı (dolu depo).</summary>
    public const int Max = 12;

    /// <summary>Harici sözleşmenin üst sınırı (yüzde).</summary>
    public const int MaxPercent = 100;

    public static bool IsValid(int value) => value is >= 0 and <= Max;

    /// <summary>Yüzde (0–100) → on ikide bir (0–12), en yakına. Aralık dışı girdi çağıranın sorumluluğu
    /// (önce doğrula); yine de sonuç 0–12'ye kıstırılır.</summary>
    public static int PercentToTwelfths(int percent)
        => Math.Clamp((Math.Clamp(percent, 0, MaxPercent) * Max + MaxPercent / 2) / MaxPercent, 0, Max);

    /// <summary>On ikide bir (0–12) → yüzde (0–100), en yakına. 6 → 50, 10 → 83, 12 → 100.</summary>
    public static int TwelfthsToPercent(int inTwelfths)
        => (Math.Clamp(inTwelfths, 0, Max) * MaxPercent + Max / 2) / Max;

    /// <summary>Boş değerli (null = girilmedi) karşılık; boş kalır.</summary>
    public static int? TwelfthsToPercent(int? inTwelfths) => inTwelfths is { } v ? TwelfthsToPercent(v) : null;
}
