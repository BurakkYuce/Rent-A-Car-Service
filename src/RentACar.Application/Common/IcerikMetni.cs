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
public static class IcerikMetni
{
    /// <summary>Boş satır paragraf ayırır (CRLF/LF farkı önemsizleştirilir, uçlar kırpılır).</summary>
    public static IReadOnlyList<string> Paragraflar(string? metin)
        => string.IsNullOrWhiteSpace(metin)
            ? []
            : [.. metin.Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>İçerik bloğunun türü — <see cref="Bloklar"/> çıktısında kullanılır.</summary>
    public enum BlokTuru { Paragraf, Baslik2, Baslik3 }

    /// <summary>Tek bir içerik bloğu: türü + (işaretleyicisi ayıklanmış) metni.</summary>
    public readonly record struct Blok(BlokTuru Tur, string Metin);

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
    /// <para><see cref="Paragraflar"/> DEĞİŞMEDEN duruyor: SSS cevabı ve içerik sayfası onu
    /// kullanmaya devam eder (orada ara başlık yok).</para>
    /// </summary>
    public static IReadOnlyList<Blok> Bloklar(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return [];
        var sonuc = new List<Blok>();
        foreach (var ham in Paragraflar(metin))
        {
            // Bir blok birden çok satır olabilir; ara başlık YALNIZ bloğun ilk satırıysa geçerlidir.
            // "Fiyat ## dahil" gibi cümle içinde geçen diye başlık üretmek metni bozardı.
            var satirlar = ham.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var satir in satirlar)
            {
                if (satir.StartsWith("### ", StringComparison.Ordinal))
                    sonuc.Add(new Blok(BlokTuru.Baslik3, satir[4..].Trim()));
                else if (satir.StartsWith("## ", StringComparison.Ordinal))
                    sonuc.Add(new Blok(BlokTuru.Baslik2, satir[3..].Trim()));
                else if (sonuc.Count > 0 && sonuc[^1].Tur == BlokTuru.Paragraf && satirlar.Length > 1)
                    // Aynı bloktaki ardışık düz satırlar TEK paragrafta birleşir (yazarın niyeti bu).
                    sonuc[^1] = new Blok(BlokTuru.Paragraf, sonuc[^1].Metin + " " + satir);
                else
                    sonuc.Add(new Blok(BlokTuru.Paragraf, satir));
            }
        }
        return sonuc;
    }

    /// <summary>Gövdedeki kelime sayısı (işaretleyiciler ayıklanmış). JSON-LD <c>wordCount</c>.</summary>
    public static int KelimeSayisi(string? metin)
        => string.Join(' ', Bloklar(metin).Select(b => b.Metin))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    /// <summary>
    /// Meta açıklama / og:description için kısa özet: işaretleyiciler ayıklanmış düz metnin ilk
    /// <paramref name="uzunluk"/> karakteri, KELİME ortasından kesilmeden (sonuna "…" eklenir).
    /// </summary>
    public static string? Ozet(string? metin, int uzunluk = 160)
    {
        var duz = string.Join(' ', Bloklar(metin).Select(b => b.Metin)).Trim();
        if (duz.Length == 0) return null;
        if (duz.Length <= uzunluk) return duz;
        var kes = duz[..uzunluk];
        var bosluk = kes.LastIndexOf(' ');
        return (bosluk > uzunluk / 2 ? kes[..bosluk] : kes).TrimEnd(',', '.', ';', ':') + "…";
    }
}
