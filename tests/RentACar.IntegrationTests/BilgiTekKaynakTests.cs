using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// <c>?bilgi=</c> mesajını YALNIZ <c>MainLayout</c> basar.
///
/// <para><b>Neden var (kendi ürettiğim hata):</b> global bildirim şeridi eklenirken (08d9fc4)
/// çift-render riskini yalnız <c>?ok=</c> okuyan 27 sayfaya karşı kontrol etmiştim. Oysa
/// <c>VehicleGroupList</c> ve <c>BranchList</c> ZATEN <c>?bilgi=</c> okuyup kendi
/// <c>&lt;p class="ok"&gt;</c>'sini basıyordu — şerit eklenince o iki ekranda mesaj İKİ KEZ
/// görünmeye başladı. "Farklı anahtar kullandım, çakışma imkânsız" varsayımım o iki sayfa için
/// yanlıştı.</para>
///
/// <para>Ders: yeni bir merkezî mekanizma eklerken "bu anahtarı kim OKUYOR" sorusu, "kim
/// YAZIYOR" sorusundan daha önemlidir. Bu çit o soruyu kalıcı hale getirir.</para>
/// </summary>
public sealed class BilgiTekKaynakTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Hicbir_sayfa_bilgi_query_sini_KENDI_basmaz()
    {
        var root = RepoRoot();
        var pages = Path.Combine(root, "src/RentACar.Web/Components/Pages");

        var found = Directory
            .EnumerateFiles(pages, "*.razor", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"SupplyParameterFromQuery\(Name\s*=\s*""bilgi""\)"))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(found.Count == 0,
            "`?bilgi=` mesajını MainLayout global şerit olarak basıyor. Sayfa da basarsa kullanıcı " +
            "mesajı İKİ KEZ görür. Sayfaya özel bir mesaj gerekiyorsa farklı bir anahtar kullanın.\n  "
            + string.Join("\n  ", found));
    }

    [Fact]
    public void Layout_bilgi_yi_okuyor()
    {
        // Ters yön: şerit kaldırılırsa yukarıdaki test sessizce yeşil kalır ve `?bilgi=` döndüren
        // ~70 uç hiçbir yerde GÖRÜNMEZ olurdu.
        var layout = File.ReadAllText(Path.Combine(RepoRoot(),
            "src/RentACar.Web/Components/Layout/MainLayout.razor"));
        Assert.Contains("\"bilgi\"", layout, StringComparison.Ordinal);
        Assert.Contains("data-rc-bildirim", layout, StringComparison.Ordinal);
    }
}
