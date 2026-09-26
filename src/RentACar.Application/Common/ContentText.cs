namespace RentACar.Application.Common;

/// <summary>
/// Kullanıcının yazdığı düz metni ekrana basılacak paragraflara böler.
///
/// <para><b>HTML ÜRETMEZ.</b> Dönen her parça Razor'da <c>&lt;p&gt;@p&lt;/p&gt;</c> olarak basılır ve
/// Razor onu otomatik encode eder. <c>MarkupString</c> kullanılmadığı sürece XSS risk sınıfı tasarım
/// gereği yoktur — firma personeli gövdeye <c>&lt;script&gt;</c> yazsa ekranda metin görünür.</para>
///
/// <para>PR-16'da <c>BlogYaziDetay.razor</c>'daki özel metottan buraya çıkarıldı: aynı kural artık
/// blog yazısı, içerik sayfası ve SSS cevabı için TEK yerde. Kopyalanan render kuralı, birinde
/// düzeltilip diğerinde kalır.</para>
/// </summary>
public static class ContentText
{
    /// <summary>Boş satır paragraf ayırır (CRLF/LF farkı önemsizleştirilir, uçlar kırpılır).</summary>
    public static IReadOnlyList<string> Paragraphs(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>İçerik bloğunun türü — <see cref="Blocks"/> çıktısında kullanılır.</summary>
    public enum BlockType { Paragraf, Baslik2, Baslik3 }

    /// <summary>Tek bir içerik bloğu: türü + (işaretleyicisi ayıklanmış) metni.</summary>
    public readonly record struct Blok(BlockType Tur, string Metin);

    /// <summary>
    /// Metni ARA BAŞLIK farkındalığıyla bloklara böler: <c>##</c> ile başlayan satır h2,
    /// <c>###</c> h3, geri kalan paragraf olur.
    ///
    /// <para><b>Neden sadece bu iki işaret:</b> gövde DÜZ METİN olarak saklanıyor ve öyle kalmalı —
    /// tam Markdown desteklemek (bağlantı, resim, ham HTML) <c>MarkupString</c> gerektirir ve
    /// sınıfın en üstteki "XSS risk sınıfı tasarım gereği yoktur" güvencesini bozar. <c>##</c>
    /// yalnız bir ETİKET SEÇER (h2/h3), içerik yine encode edilerek basılır; hiçbir girdi HTML'e
    /// dönüşmez. Referans sitedeki blog gövdeleri de aynı iki işareti kullanıyor.</para>
    ///
    /// <para><see cref="Paragraphs"/> DEĞİŞMEDEN duruyor: SSS cevabı ve içerik sayfası onu
    /// kullanmaya devam eder (orada ara başlık yok).</para>
    /// </summary>
    public static IReadOnlyList<Blok> Blocks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<Blok>();
        foreach (var raw in Paragraphs(text))
        {
            // Bir blok birden çok satır olabilir; ara başlık YALNIZ bloğun ilk satırıysa geçerlidir.
            // "Fiyat ## dahil" gibi cümle içinde geçen diye başlık üretmek metni bozardı.
            var rows = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var row in rows)
            {
                if (row.StartsWith("### ", StringComparison.Ordinal))
                    result.Add(new Blok(BlockType.Baslik3, row[4..].Trim()));
                else if (row.StartsWith("## ", StringComparison.Ordinal))
                    result.Add(new Blok(BlockType.Baslik2, row[3..].Trim()));
                else if (result.Count > 0 && result[^1].Tur == BlockType.Paragraf && rows.Length > 1)
                    // Aynı bloktaki ardışık düz satırlar TEK paragrafta birleşir (yazarın niyeti bu).
                    result[^1] = new Blok(BlockType.Paragraf, result[^1].Metin + " " + row);
                else
                    result.Add(new Blok(BlockType.Paragraf, row));
            }
        }
        return result;
    }

    /// <summary>Gövdedeki kelime sayısı (işaretleyiciler ayıklanmış). JSON-LD <c>wordCount</c>.</summary>
    public static int WordCount(string? text)
        => string.Join(' ', Blocks(text).Select(b => b.Metin))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    /// <summary>
    /// Meta açıklama / og:description için kısa özet: işaretleyiciler ayıklanmış düz metnin ilk
    /// <paramref name="length"/> karakteri, KELİME ortasından kesilmeden (sonuna "…" eklenir).
    /// </summary>
    public static string? Summary(string? text, int length = 160)
    {
        var duz = string.Join(' ', Blocks(text).Select(b => b.Metin)).Trim();
        if (duz.Length == 0) return null;
        if (duz.Length <= length) return duz;
        var issue = duz[..length];
        var whitespace = issue.LastIndexOf(' ');
        return (whitespace > length / 2 ? issue[..whitespace] : issue).TrimEnd(',', '.', ';', ':') + "…";
    }
}
