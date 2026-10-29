namespace RentACar.Application.TenantSettings;

/// <summary>
/// Halka açık sitenin seçilebilir temaları (<c>TenantSettings.SiteTemasi</c>). Anahtarlar KALICI: DB'de
/// saklanır ve sitenin `?tema=` önizleme parametresinde kullanılır. Yeni tema eklenirse hem buraya hem
/// PublicSite'ın <c>SiteThemes</c> kaydına eklenir (yapısal test ikisinin aynı olduğunu kilitler).
///
/// <para><b>Anahtar göçleri:</b> (1) 2026-10 başı: <c>tarife</c> → <c>pazar</c>, <c>kontuar</c> → <c>rota</c>
/// (<c>RenameSiteThemeKeys</c>). (2) 2026-10 sonu (T4): Pazar, Rota ve Vitrin temaları söküldü — <c>pazar</c> →
/// <c>anahtar</c>, <c>rota</c> → <c>kiyas</c>, <c>vitrin</c> → <c>paket</c> (<c>RetireOldSiteThemes</c>; temaya ait
/// olmayan kayıtlı palet NULL'a çekildi). Eski anahtarlar <see cref="LegacyAliases"/> ile KALICI olarak yeni anahtara
/// normalize edilir (paylaşılmış <c>?tema=</c> bağlantıları, eski istemcinin PUT'u, migration'dan önce okunan değer).
/// Eski anahtar asla saklanmaz ve geçerli değer listesinde (<see cref="All"/>) görünmez.</para>
/// </summary>
public static class SiteThemeKeys
{
    public const string Anahtar = "anahtar";
    public const string Kiyas = "kiyas";
    public const string Paket = "paket";

    /// <summary>Seçilmemiş / bilinmeyen tema → bu.</summary>
    public const string Default = Anahtar;

    /// <summary>Geçerli (saklanabilir) anahtarlar — sıra ERP'deki seçenek sırasıdır (varsayılan başta).</summary>
    public static readonly IReadOnlyList<string> All = [Anahtar, Kiyas, Paket];

    /// <summary>Eski anahtar → yeni anahtar. KALICI: silinmez (eski bağlantılar ve önizleme adresleri yaşar). Zincir
    /// yok: her eski anahtar doğrudan bugünkü temaya gider (tarife → pazar → anahtar gibi ara adım tutulmaz).</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pazar"] = Anahtar,
        ["tarife"] = Anahtar,
        ["rota"] = Kiyas,
        ["kontuar"] = Kiyas,
        ["vitrin"] = Paket,
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
