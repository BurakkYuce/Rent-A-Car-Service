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
public static class TemplateFiller
{
    /// <summary>Şablonda kullanılabilecek yer tutucular (Ayarlar ekranında listelenir).</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "MusteriAd", "Firma", "No", "Plaka", "Arac",
        "CikisTarih", "DonusTarih", "CikisOfis", "DonusOfis", "Tutar",
    ];

    /// <param name="htmlEscape">E-posta (HTML) gövdesi için true, SMS (düz metin) için false.</param>
    public static string Fill(
        string? template, IReadOnlyDictionary<string, string?> values, bool htmlEscape)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var sb = new StringBuilder(template.Length + 64);
        var i = 0;
        while (i < template.Length)
        {
            var opening = template.IndexOf('{', i);
            if (opening < 0) { sb.Append(template, i, template.Length - i); break; }

            var closing = template.IndexOf('}', opening + 1);
            if (closing < 0) { sb.Append(template, i, template.Length - i); break; }

            sb.Append(template, i, opening - i);
            var name = template[(opening + 1)..closing];

            if (values.TryGetValue(name, out var value))
            {
                var text = value ?? string.Empty;
                sb.Append(htmlEscape ? System.Net.WebUtility.HtmlEncode(text) : text);
            }
            else
            {
                // Bilinmeyen anahtar: süslü parantezlerle birlikte aynen korunur.
                sb.Append(template, opening, closing - opening + 1);
            }
            i = closing + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// HTML gövdeden okunabilir düz metin alternatifi üretir (çok parçalı e-postanın text kısmı).
    /// Basit ve kasıtlı olarak muhafazakârdır: satır sonu üreten etiketler yeni satıra çevrilir,
    /// kalan etiketler atılır, HTML varlıkları çözülür.
    /// </summary>
    public static string PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var s = System.Text.RegularExpressions.Regex.Replace(
            html, @"<\s*(br|/p|/div|/li|/tr|/h[1-6])\s*/?\s*>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", string.Empty);
        s = System.Net.WebUtility.HtmlDecode(s);

        // Ardışık boş satırları teke indir; baş/son boşlukları at.
        var rows = s.Replace("\r", string.Empty).Split('\n').Select(x => x.Trim());
        var output = new List<string>();
        foreach (var row in rows)
        {
            if (row.Length == 0 && (output.Count == 0 || output[^1].Length == 0)) continue;
            output.Add(row);
        }
        while (output.Count > 0 && output[^1].Length == 0) output.RemoveAt(output.Count - 1);
        return string.Join('\n', output);
    }
}
