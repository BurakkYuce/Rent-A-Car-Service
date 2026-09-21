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
public static class PanelSekme
{
    public const string Gecikmis = "gec";
    public const string Bugun = "bugun";
    public const string Yarin = "yarin";

    /// <summary>Kullanıcının URL'de AÇIKÇA seçtiği sekme (kanonik değer); seçmediyse <c>null</c>.
    /// Linklere bu yazılır: diğer kartın seçimsiz hâli seçimsiz kalmalı ki orada da varsayılan
    /// kural her yüklemede yeniden işlesin (ör. gecikmiş çıkışlar kapanınca Bugün'e dönsün).</summary>
    public static string? Secilen(string? ham)
    {
        if (string.Equals(ham, Gecikmis, StringComparison.OrdinalIgnoreCase)) return Gecikmis;
        if (string.Equals(ham, Bugun, StringComparison.OrdinalIgnoreCase)) return Bugun;
        if (string.Equals(ham, Yarin, StringComparison.OrdinalIgnoreCase)) return Yarin;
        return null;
    }

    /// <summary>DÖNÜŞLER kartında ekranda GERÇEKTEN açık olan sekme: çip vurgusu ve liste bunu
    /// kullanır (ham değeri değil — ikisi ayrışırsa vurgulu çip ile görünen liste farklı olur).
    /// Tahsil Et dönüş adresi ise ETKİN değeri değil <see cref="Secilen"/>'i taşır: etkin "gec"
    /// yazılsaydı seçimsiz pano açık seçime döner, gecikmişler kapanınca 120 sn tazeleme kartı
    /// "Gecikmiş 0 — Kayıt yok."ta tutardı. Tahsilat kiranın durumunu değiştirmediği için seçimsiz
    /// dönüş de aynı sekmeyi açar.</summary>
    public static string Etkin(string? ham, int gecikmisSayisi)
        => Secilen(ham) ?? (gecikmisSayisi > 0 ? Gecikmis : Bugun);

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
    public static string CikisEtkin(string? ham) => Secilen(ham) ?? Bugun;

    /// <summary>Çipin CSS sınıfı. <c>on</c> = etkin sekme. <c>acil</c> = gecikmiş kayıt var —
    /// hangi sekme açık olursa olsun Gecikmiş çipinde basılır (kullanıcı Bugün/Yarın'a bakarken de
    /// bekleyen gecikmiş işi görmeli; eskiden gri "Yarın 0" ile ayırt edilemiyordu).</summary>
    public static string CipSinifi(string sekme, string etkinSekme, int gecikmisSayisi)
    {
        var on = sekme == etkinSekme;
        var acil = sekme == Gecikmis && gecikmisSayisi > 0;
        return (on, acil) switch
        {
            (true, true) => "on acil",
            (true, false) => "on",
            (false, true) => "acil",
            _ => "",
        };
    }
}
