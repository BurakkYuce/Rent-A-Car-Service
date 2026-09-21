namespace RentACar.Application.Common;

/// <summary>
/// Türkçe metin normalizasyonu — slug üretimi (Blog, roadmap PR-6) VE serbest-metin grup
/// eşleşmesi (FleetShowcaseService, PR-4.5) için TEK ortak kaynak. `StringComparer.OrdinalIgnoreCase`
/// Türkçe'ye duyarsızdır: `İ` (U+0130) ile `i` bu comparer altında eşleşmez (`i`'nin invariant büyüğü
/// `I` U+0049'dur, `İ` ayrı bir karakter) — "DİZEL"/"dizel" gibi gerçek veriler sessizce kaçırılır.
/// `tr-TR` culture-aware comparer teknik olarak çalışır ama container/OS locale erişilebilirliğine
/// bağımlı, taşınabilir değil. Bunun yerine ÖNCE transliterasyon (hem büyük hem küçük Türkçe harfler),
/// SONRA ordinal karşılaştırma — deterministik ve taşınabilir.
/// </summary>
public static class TurkishText
{
    private static readonly (char From, char To)[] Map =
    [
        ('Ş', 's'), ('ş', 's'),
        ('Ç', 'c'), ('ç', 'c'),
        ('Ğ', 'g'), ('ğ', 'g'),
        ('Ü', 'u'), ('ü', 'u'),
        ('Ö', 'o'), ('ö', 'o'),
        ('İ', 'i'), ('I', 'i'), ('ı', 'i'),
    ];

    /// <summary>
    /// Çevrim tablosu — F1.6: SQL karşılığı (<c>Infrastructure/Persistence/TrSql</c>, seçim uçlarının
    /// DB'de sınırlı Türkçe araması) bu tablodan üretilir; C# ve SQL kuralı ayrışamaz. Neden SQL'de
    /// <c>lower()</c>/ILIKE yetmez: sonucu DB'nin LC_CTYPE'ına bağlı — <c>en_US</c>'de <c>lower('I')='i'</c>
    /// olduğundan "IŞIK" ile "ışık" eşleşmez. Önce ASCII'ye çevrim, sonra küçültme her yerelde aynıdır.
    /// </summary>
    public static IReadOnlyList<(char From, char To)> Esleme => Map;

    /// <summary>Türkçe harfleri (büyük+küçük) ASCII hedefe eşler, sonra kalan A-Z'yi invariant küçültür.</summary>
    public static string Normalize(string s)
    {
        var arr = s.ToCharArray();
        for (var i = 0; i < arr.Length; i++)
        {
            foreach (var (from, to) in Map)
            {
                if (arr[i] == from) { arr[i] = to; break; }
            }
        }
        return new string(arr).ToLowerInvariant();
    }

    /// <summary>Normalize edip ordinal karşılaştırır — `null` yalnız `null`'a eşittir.</summary>
    public static bool EqualsIgnoreTurkishCase(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }

    /// <summary>
    /// Türkçe-doğru URL slug'ı: <see cref="Normalize"/> (İ/I/ı dahil) → alfanümerik dışı her şey tire →
    /// ardışık/kenar tireler sadeleşir. Boş dönebilir (çağıran karar verir).
    /// PR-14: Blog'un `Slugify`'ı buraya taşındı — ilan adresleri de aynı kuralı kullanıyor,
    /// iki kopya olsaydı "aynı başlık iki farklı adres" üretirdi.
    /// </summary>
    public static string Slugify(string s)
    {
        var normalized = Normalize(s);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
