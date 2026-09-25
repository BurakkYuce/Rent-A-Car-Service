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
public static class YakitOlcegi
{
    /// <summary>İç ölçeğin üst sınırı (dolu depo).</summary>
    public const int EnFazla = 12;

    /// <summary>Harici sözleşmenin üst sınırı (yüzde).</summary>
    public const int YuzdeEnFazla = 100;

    public static bool Gecerli(int deger) => deger is >= 0 and <= EnFazla;

    /// <summary>Yüzde (0–100) → on ikide bir (0–12), en yakına. Aralık dışı girdi çağıranın sorumluluğu
    /// (önce doğrula); yine de sonuç 0–12'ye kıstırılır.</summary>
    public static int YuzdedenOnIkiye(int yuzde)
        => Math.Clamp((Math.Clamp(yuzde, 0, YuzdeEnFazla) * EnFazla + YuzdeEnFazla / 2) / YuzdeEnFazla, 0, EnFazla);

    /// <summary>On ikide bir (0–12) → yüzde (0–100), en yakına. 6 → 50, 10 → 83, 12 → 100.</summary>
    public static int OnIkidenYuzdeye(int onIkide)
        => (Math.Clamp(onIkide, 0, EnFazla) * YuzdeEnFazla + EnFazla / 2) / EnFazla;

    /// <summary>Boş değerli (null = girilmedi) karşılık; boş kalır.</summary>
    public static int? OnIkidenYuzdeye(int? onIkide) => onIkide is { } v ? OnIkidenYuzdeye(v) : null;
}
