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
}
