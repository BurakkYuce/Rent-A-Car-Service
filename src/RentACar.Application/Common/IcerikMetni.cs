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
}
