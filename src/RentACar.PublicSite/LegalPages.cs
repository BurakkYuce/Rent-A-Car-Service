using RentACar.Application.SiteIcerik;

namespace RentACar.PublicSite;

/// <summary>
/// Yasal metinlerin (gizlilik / KVKK aydınlatma / kullanım koşulları / çerez politikası)
/// halka açık sitedeki YERİNİ bulan yardımcı.
///
/// <para><b>Neden ayrı bir Razor sayfası yok:</b> site zaten genel amaçlı bir içerik-sayfası
/// sistemine sahip (<c>Components/Pages/Sayfa.razor</c> + <c>SiteIcerikService</c>). Yasal metinler
/// firmadan firmaya değişir (unvan, VKN, veri sorumlusu, saklama süreleri) ve zaman içinde güncellenir —
/// bunları koda gömmek her güncellemede DEPLOY gerektirirdi. Ayrıca kod içindeki sabit bir metin,
/// hukuk danışmanının onayladığı metnin yerine geçemez. Dolayısıyla yasal sayfa = ERP'den girilen
/// normal bir "site sayfası"; buradaki tek iş, o sayfaları TANIYIP alt bilgide öne çıkarmak.</para>
///
/// <para><b>Tanıma slug ANAHTARIYLA yapılır, tam eşleşmeyle değil:</b> personel başlığı serbest yazar
/// ("Gizlilik Politikası", "Gizlilik ve KVKK", "Çerez Politikamız") ve slug başlıktan
/// <c>TurkishText.Slugify</c> ile türetilir — çıktı daima küçük harf ASCII olur, bu yüzden
/// <see cref="StringComparison.Ordinal"/> yeterlidir. Tam-eşleşme listesi tutsaydık başlığa eklenen
/// tek bir kelime bağlantıyı SESSİZCE düşürürdü.</para>
///
/// <para><b>Yanlış-pozitif riski bilinçli kabul edildi:</b> "cerez" içeren bir pazarlama sayfası
/// (pratikte yok) yasal sütuna düşer. Bunun bedeli yalnızca alt bilgideki sütun seçimidir; sayfa
/// yine açılır. Ters yön (yasal sayfanın alt bilgide hiç görünmemesi) çok daha kötü olurdu.</para>
/// </summary>
public static class LegalPages
{
    /// <summary>Bir sayfayı "yasal" yapan slug parçaları (hepsi Slugify çıktısı biçiminde).</summary>
    private static readonly string[] Keys =
    [
        "gizlilik", "kvkk", "kisisel-veri", "aydinlatma",
        "kullanim-kosullari", "kullanim-sartlari", "kullanici-sozlesmesi", "uyelik-sozlesmesi",
        "cerez", "mesafeli-satis", "iptal-iade",
    ];

    /// <summary>Alt bilgide "Yasal" sütununa mı düşecek?</summary>
    public static bool Legal(SayfaOzet page) => Legal(page.Slug);

    public static bool Legal(string? slug) =>
        !string.IsNullOrEmpty(slug) && Array.Exists(Keys, a => slug.Contains(a, StringComparison.Ordinal));

    /// <summary>
    /// Çerez şeridindeki "Ayrıntılar" bağlantısının hedefi. Sırayla: çerez politikası → gizlilik →
    /// KVKK aydınlatma. Hiçbiri YAYINDA değilse <c>null</c> döner ve şerit bağlantıyı hiç basmaz —
    /// yayında olmayan sayfaya link vermek 404 üretirdi (<c>Sayfa.razor</c> gerçek 404 döndürür).
    /// </summary>
    /// <param name="publishedPages">Yalnız yayındaki sayfalar (repo zaten <c>Yayinda</c> süzüyor).</param>
    public static string? CookiePolicySlug(IReadOnlyList<SayfaOzet> publishedPages)
    {
        foreach (var key in (string[])["cerez", "gizlilik", "kvkk", "kisisel-veri"])
        {
            var found = publishedPages.FirstOrDefault(s => s.Slug.Contains(key, StringComparison.Ordinal));
            if (found is not null) return found.Slug;
        }

        return null;
    }
}
