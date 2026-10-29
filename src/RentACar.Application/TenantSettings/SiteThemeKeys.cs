namespace RentACar.Application.TenantSettings;

/// <summary>
/// Halka açık sitenin seçilebilir temaları (<c>TenantSettings.SiteTemasi</c>). Anahtarlar KALICI: DB'de
/// saklanır ve sitenin `?tema=` önizleme parametresinde kullanılır. Yeni tema eklenirse hem buraya hem
/// PublicSite'ın <c>SiteThemes</c> kaydına eklenir (yapısal test ikisinin aynı olduğunu kilitler).
///
/// <para><b>Anahtar göçü (2026-10):</b> <c>tarife</c> → <c>pazar</c>, <c>kontuar</c> → <c>rota</c>. Kayıtlı değerler
/// <c>RenameSiteThemeKeys</c> migration'ıyla çevrildi; eski anahtarlar <see cref="LegacyAliases"/> ile KALICI olarak
/// yeni anahtara normalize edilir (paylaşılmış <c>?tema=tarife</c> bağlantıları, eski istemcinin PUT'u, migration'dan
/// önce okunan değer). Eski anahtar asla saklanmaz ve geçerli değer listesinde (<see cref="All"/>) görünmez.</para>
/// </summary>
public static class SiteThemeKeys
{
    public const string Anahtar = "anahtar";
    public const string Pazar = "pazar";
    public const string Vitrin = "vitrin";
    public const string Rota = "rota";

    /// <summary>Seçilmemiş / bilinmeyen tema → bu. 2026-10'dan beri <c>anahtar</c> (önce <c>pazar</c>): kayıtlı
    /// teması olan firma etkilenmez; teması boş (null) olan firma Anahtar'ı görür.</summary>
    public const string Default = Anahtar;

    /// <summary>Geçerli (saklanabilir) anahtarlar — sıra ERP'deki seçenek sırasıdır (varsayılan başta).</summary>
    public static readonly IReadOnlyList<string> All = [Anahtar, Pazar, Vitrin, Rota];

    /// <summary>Eski anahtar → yeni anahtar. KALICI: silinmez (eski bağlantılar ve önizleme adresleri yaşar).</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tarife"] = Pazar,
        ["kontuar"] = Rota,
    };

    /// <summary>Kanonik anahtar ya da null (tanınmayan/boş). Eski anahtar yeni karşılığına çevrilir.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        if (All.Contains(v)) return v;
        return LegacyAliases.TryGetValue(v, out var current) ? current : null;
    }
}
