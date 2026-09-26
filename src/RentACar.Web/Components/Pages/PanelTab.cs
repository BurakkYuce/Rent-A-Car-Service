namespace RentACar.Web.Components.Pages;

/// <summary>
/// Panel "Dönüşler" / "Çıkışlar" kartlarında hangi gün-sekmesinin (Gecikmiş/Bugün/Yarın) açılacağı.
///
/// <para><b>Neden var (canlı kanıt):</b> Dönüşler kartında 9 gecikmiş dönüş (en eskisi 08.07.2026)
/// varken pano "Bugün 0 — Kayıt yok." ile açılıyordu; "Gecikmiş 9" çipi de "Yarın 0" ile aynı gri
/// görünüyordu. Yani en acil iş, kullanıcı bir sekmeye tıklamadıkça görünmüyordu. Karar sayfa
/// içinde üç yere dağılmıştı (çip vurgusu, liste, Tahsil Et dönüş adresi) ve üçü ham <c>null</c>'ı
/// ayrı ayrı yorumluyordu; burada TEK saf fonksiyona indirildi, test doğruluk tablosuyla kilitli.</para>
///
/// <para>Kural: kullanıcı URL'de sekme SEÇTİYSE o DAİMA kazanır. Seçmediyse ve gecikmiş kayıt varsa
/// "gec", yoksa "bugun". Seçim yok = parametre yok, boş (<c>?cf=</c> — çip linkleri diğer kartın
/// seçimsiz hâlini böyle taşır) veya tanınmayan değer. Tanınmayanı "seçim yok" saymak, çip vurgusu
/// ile listenin aynı kararı vermesini garanti eder (eskiden tanınmayan değerde hiçbir çip yanmıyor,
/// liste ise Bugün'ü gösteriyordu).</para>
/// </summary>
public static class PanelTab
{
    public const string Overdue = "gec";
    public const string Today = "bugun";
    public const string Tomorrow = "yarin";

    /// <summary>Kullanıcının URL'de AÇIKÇA seçtiği sekme (kanonik değer); seçmediyse <c>null</c>.
    /// Linklere bu yazılır: diğer kartın seçimsiz hâli seçimsiz kalmalı ki orada da varsayılan
    /// kural her yüklemede yeniden işlesin (ör. gecikmiş çıkışlar kapanınca Bugün'e dönsün).</summary>
    public static string? Selected(string? raw)
    {
        if (string.Equals(raw, Overdue, StringComparison.OrdinalIgnoreCase)) return Overdue;
        if (string.Equals(raw, Today, StringComparison.OrdinalIgnoreCase)) return Today;
        if (string.Equals(raw, Tomorrow, StringComparison.OrdinalIgnoreCase)) return Tomorrow;
        return null;
    }

    /// <summary>DÖNÜŞLER kartında ekranda GERÇEKTEN açık olan sekme: çip vurgusu ve liste bunu
    /// kullanır (ham değeri değil — ikisi ayrışırsa vurgulu çip ile görünen liste farklı olur).
    /// Tahsil Et dönüş adresi ise ETKİN değeri değil <see cref="Selected"/>'i taşır: etkin "gec"
    /// yazılsaydı seçimsiz pano açık seçime döner, gecikmişler kapanınca 120 sn tazeleme kartı
    /// "Gecikmiş 0 — Kayıt yok."ta tutardı. Tahsilat kiranın durumunu değiştirmediği için seçimsiz
    /// dönüş de aynı sekmeyi açar.</summary>
    public static string Active(string? raw, int overdueCount)
        => Selected(raw) ?? (overdueCount > 0 ? Overdue : Today);

    /// <summary>
    /// ÇIKIŞLAR kartında açık olan sekme: seçim yoksa gecikmiş sayısına BAKILMAZ, daima "bugun".
    ///
    /// <para><b>Neden Dönüşler'den farklı (adversarial bulgu):</b> gecikmiş DÖNÜŞ, müşterinin
    /// elindeki araçtır — o gün kovalanacak iş. Gecikmiş ÇIKIŞ ise hiç gelmemiş (no-show) açık
    /// rezervasyondur ve onu kapatan bir job yoktur: Rezerv/Onaylı kalıp tarihi geçmiş her kayıt
    /// kalıcı olarak "gecikmiş" sayılır (yerel DB'de 2,5 aylık bir no-show vardı). Varsayılan "gec"
    /// olsaydı kart her açılışta bayat kayıtla açılır, günün asıl işi olan Bugün'ün çıkışları
    /// gizlenirdi. Bekleyen no-show yine görünür: Gecikmiş çipi kırmızı (acil) kalır ve sayı
    /// "Görülmeyen Rez." kutusunda da yazar.</para>
    /// </summary>
    public static string IsPickupActive(string? raw) => Selected(raw) ?? Today;

    /// <summary>Çipin CSS sınıfı. <c>on</c> = etkin sekme. <c>acil</c> = gecikmiş kayıt var —
    /// hangi sekme açık olursa olsun Gecikmiş çipinde basılır (kullanıcı Bugün/Yarın'a bakarken de
    /// bekleyen gecikmiş işi görmeli; eskiden gri "Yarın 0" ile ayırt edilemiyordu).</summary>
    public static string ChipClass(string tab, string activeTab, int overdueCount)
    {
        var on = tab == activeTab;
        var urgent = tab == Overdue && overdueCount > 0;
        return (on, acil: urgent) switch
        {
            (true, true) => "on acil",
            (true, false) => "on",
            (false, true) => "acil",
            _ => "",
        };
    }
}
