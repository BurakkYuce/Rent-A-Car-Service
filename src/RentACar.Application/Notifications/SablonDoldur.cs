using System.Text;

namespace RentACar.Application.Notifications;

/// <summary>
/// Şablon yer tutucularını doldurur. SAF ve <c>public</c> — bağımsız oracle ile doğrudan test edilir.
///
/// <para><b>Bilinmeyen yer tutucu OLDUĞU GİBİ bırakılır.</b> Sessizce boşa çevirmek, müşteriye giden
/// metinde fark edilmeyen boşluklar üretirdi ("Sayın , aracınız hazır"); olduğu gibi kalması yazım
/// hatasını görünür kılar ve firma şablonu düzeltir.</para>
///
/// <para><b>HTML kaçışı kanala bağlıdır.</b> E-posta gövdesi HTML'dir ve değerler MÜŞTERİ VERİSİDİR
/// (ad, adres, not) — kaçışsız yerleştirmek, müşteri adına yazılmış bir etiketin e-postada
/// çalışmasına yol açar. Şablonun kendi HTML'i firmanındır ve korunur; yalnız DEĞERLER kaçırılır.
/// SMS düz metindir, kaçış uygulanmaz (aksi halde "Yüce &amp;amp; Ortakları" görünürdü).</para>
/// </summary>
public static class SablonDoldur
{
    /// <summary>Şablonda kullanılabilecek yer tutucular (Ayarlar ekranında listelenir).</summary>
    public static readonly IReadOnlyList<string> Anahtarlar =
    [
        "MusteriAd", "Firma", "No", "Plaka", "Arac",
        "CikisTarih", "DonusTarih", "CikisOfis", "DonusOfis", "Tutar",
    ];

    /// <param name="htmlKacis">E-posta (HTML) gövdesi için true, SMS (düz metin) için false.</param>
    public static string Doldur(
        string? sablon, IReadOnlyDictionary<string, string?> degerler, bool htmlKacis)
    {
        if (string.IsNullOrEmpty(sablon)) return string.Empty;

        var sb = new StringBuilder(sablon.Length + 64);
        var i = 0;
        while (i < sablon.Length)
        {
            var acilis = sablon.IndexOf('{', i);
            if (acilis < 0) { sb.Append(sablon, i, sablon.Length - i); break; }

            var kapanis = sablon.IndexOf('}', acilis + 1);
            if (kapanis < 0) { sb.Append(sablon, i, sablon.Length - i); break; }

            sb.Append(sablon, i, acilis - i);
            var ad = sablon[(acilis + 1)..kapanis];

            if (degerler.TryGetValue(ad, out var deger))
            {
                var metin = deger ?? string.Empty;
                sb.Append(htmlKacis ? System.Net.WebUtility.HtmlEncode(metin) : metin);
            }
            else
            {
                // Bilinmeyen anahtar: süslü parantezlerle birlikte aynen korunur.
                sb.Append(sablon, acilis, kapanis - acilis + 1);
            }
            i = kapanis + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// HTML gövdeden okunabilir düz metin alternatifi üretir (çok parçalı e-postanın text kısmı).
    /// Basit ve kasıtlı olarak muhafazakârdır: satır sonu üreten etiketler yeni satıra çevrilir,
    /// kalan etiketler atılır, HTML varlıkları çözülür.
    /// </summary>
    public static string DuzMetin(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var s = System.Text.RegularExpressions.Regex.Replace(
            html, @"<\s*(br|/p|/div|/li|/tr|/h[1-6])\s*/?\s*>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", string.Empty);
        s = System.Net.WebUtility.HtmlDecode(s);

        // Ardışık boş satırları teke indir; baş/son boşlukları at.
        var satirlar = s.Replace("\r", string.Empty).Split('\n').Select(x => x.Trim());
        var cikti = new List<string>();
        foreach (var satir in satirlar)
        {
            if (satir.Length == 0 && (cikti.Count == 0 || cikti[^1].Length == 0)) continue;
            cikti.Add(satir);
        }
        while (cikti.Count > 0 && cikti[^1].Length == 0) cikti.RemoveAt(cikti.Count - 1);
        return string.Join('\n', cikti);
    }
}
