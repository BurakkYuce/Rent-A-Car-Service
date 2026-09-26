using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// <b>F13 Exit çiti (kaynak taraması):</b> Blazor söküldükten sonra Web projesinde yazma ucu (POST/PUT/PATCH/DELETE)
/// yalnız <c>/api/ui/v1</c> (<c>src/RentACar.Web/Api/</c>) ve oturum uçlarındadır; <c>@page</c> yalnız bilinen kabuk
/// sayfalarında. Kalan uçların uç bazında yetki kararı <c>NonApiEndpointAuthorizationTests</c>'te (gerçek host metadatası).
///
/// <para><b>Önceki anlamı (F13.1a'ya kadar):</b> "grup kapısından dar izin isteyen her Blazor POST ucunun tetikleyici
/// razor ekranı o dar izinle kapılı" kilidi (canlı hata 2026-08-26: 13/13 dar uç yanlış kapıdaydı). Blazor ekranları ve
/// form uçları silindiği için eşleşecek ekran/uç kalmadı. Aynı kural yeni arayüzde <c>UiDugmeIzinTests</c>'te
/// (<c>DUGME_IZINLERI</c> ↔ ucun izni) ve her <c>/api/ui</c> ucunun <c>IzinMetadata</c>'sında (<c>UiApiTests</c>) kilitli.</para>
/// </summary>
public sealed class UcIzinKapsamaTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    /// <summary>Web projesinin <c>Api/</c> dışındaki C# kaynakları (derleme çıktıları hariç).</summary>
    private static IEnumerable<(string Rel, string Text)> NonApiSources(string root)
    {
        var web = Path.Combine(root, "src/RentACar.Web");
        foreach (var file in Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(web, file).Replace('\\', '/');
            if (rel.StartsWith("Api/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)
                || rel.StartsWith("bin/", StringComparison.Ordinal)) continue;
            yield return (rel, File.ReadAllText(file));
        }
    }

    private static readonly Regex WriteMap = new(@"\.Map(Post|Put|Patch|Delete)\(\s*""(?<rota>[^""]*)""");

    /// <summary>
    /// Gerekçeli izin listesi: yalnız makine webhook'u (Grafana alarm köprüsü; anonim, gizli anahtar kapılı, kullanıcı
    /// arayüzü değil). Eski form çıkış ucu (<c>/auth/logout</c>) F13 sonrası güvenlik düzeltmesinde kalktı (CSRF'siz).
    /// </summary>
    private static readonly HashSet<string> AllowedWrites = new(StringComparer.Ordinal)
    {
        "Program.cs /internal/alert",
    };

    [Fact]
    public void Web_write_endpoints_only_in_api_ui_and_session()
    {
        var found = NonApiSources(RepoRoot())
            .SelectMany(s => WriteMap.Matches(s.Text).Select(m => $"{s.Rel} {m.Groups["rota"].Value}"))
            .ToList();
        var extra = found.Where(f => !AllowedWrites.Contains(f)).ToList();
        Assert.True(extra.Count == 0, "Api/ dışında yazma ucu (Blazor form ucu geri mi geldi?):\n  " + string.Join("\n  ", extra));
        Assert.Contains("Program.cs /internal/alert", found); // tarama çalışıyor (boş kümede sessiz yeşil olmasın)
    }

    /// <summary>
    /// F13 Exit: <c>@page</c> = 0 — Web projesinde hiç razor bileşeni yok (kabuk dahil; hata/404/yetkisiz SPA'da).
    /// PublicSite ayrı bir uygulamadır, kapsam dışı.
    /// </summary>
    [Fact]
    public void No_blazor_component_remains_in_web()
    {
        var web = Path.Combine(RepoRoot(), "src/RentACar.Web");
        var razors = Directory.EnumerateFiles(web, "*.razor", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(web, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("obj/", StringComparison.Ordinal) && !f.StartsWith("bin/", StringComparison.Ordinal))
            .ToList();
        Assert.True(razors.Count == 0, "Web'de Blazor bileşeni kaldı: " + string.Join(", ", razors));
        Assert.False(Directory.Exists(Path.Combine(web, "wwwroot")), "Blazor statik dosyaları (wwwroot) kaldı.");
    }
}
