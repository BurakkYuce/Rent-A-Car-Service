using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// <b>F13 Exit çiti (kaynak taraması):</b> Blazor söküldükten sonra Web projesinde yazma ucu (POST/PUT/PATCH/DELETE)
/// yalnız <c>/api/ui/v1</c> (<c>src/RentACar.Web/Api/</c>) ve oturum uçlarındadır; kalan minimal-API GET'lerinin her biri
/// açık bir yetki kararı taşır; <c>@page</c> yalnız bilinen kabuk sayfalarında.
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
    /// Gerekçeli izin listesi: oturum uçları (F13.1b <c>/auth/*</c>'ı SPA'ya bağlar) ve makine webhook'u (Grafana alarm
    /// köprüsü; anonim, gizli anahtar kapılı, kullanıcı arayüzü değil).
    /// </summary>
    private static readonly HashSet<string> AllowedWrites = new(StringComparer.Ordinal)
    {
        "Identity/AuthEndpoints.cs /auth/login",
        "Identity/AuthEndpoints.cs /auth/logout",
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

    [Fact]
    public void Remaining_non_api_GET_endpoints_declare_authorization()
    {
        var get = new Regex(@"\.MapGet\(\s*""(?<rota>[^""]*)""");
        var gates = new Regex(@"\.(RequirePermission|RequireAuthorization|AllowAnonymous|RequireAnyPermission)\(");
        var findings = new List<string>();
        var count = 0;
        foreach (var (rel, text) in NonApiSources(RepoRoot()))
        {
            if (rel is "Program.cs" or "Spa/SpaHosting.cs") continue; // sağlık uçları + SPA kabuğu (anonim; ayrı testler)
            var starts = get.Matches(text);
            for (var i = 0; i < starts.Count; i++)
            {
                count++;
                // Ucun kendi zinciri ya da dosyadaki grup tanımı (MapGroup(...).RequireX) kapı taşımalı.
                var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
                var window = text[starts[i].Index..end];
                if (!gates.IsMatch(window) && !Regex.IsMatch(text, @"MapGroup\([^;]*\.(RequirePermission|RequireAuthorization)\("))
                    findings.Add($"{rel} {starts[i].Groups["rota"].Value}");
            }
        }
        Assert.True(count >= 5, $"Tarama şüpheli: {count} GET ucu.");
        Assert.True(findings.Count == 0, "Yetki kararı açık olmayan GET ucu:\n  " + string.Join("\n  ", findings));
    }

    /// <summary>
    /// <c>@page</c> yalnız kabuk sayfalarında (hata/404/yetkisiz/doğrulama hatası); F13.1b bunları da SPA'ya bağlayıp
    /// sayıyı 0'a indirir.
    /// </summary>
    [Fact]
    public void Blazor_pages_only_shell_pages_remain()
    {
        var components = Path.Combine(RepoRoot(), "src/RentACar.Web/Components");
        var pages = Directory.Exists(components)
            ? Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^@page\s", RegexOptions.Multiline))
                .Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : [];
        Assert.True(pages.All(p => p is "DogrulamaHatasi.razor" or "Error.razor" or "NotFound.razor" or "Yetkisiz.razor"),
            "Beklenmeyen Blazor sayfası: " + string.Join(", ", pages));
    }
}
