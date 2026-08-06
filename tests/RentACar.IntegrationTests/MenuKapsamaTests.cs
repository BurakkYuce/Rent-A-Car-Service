using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// Menü kapsama çiti: yazılmış ama menüye eklenmemiş sayfa kalmasın.
///
/// <para><b>Neden var:</b> 2026-08-06 parite taraması `/raporlar/kasa-banka` ve
/// `/raporlar/servis-ozet` rotalarının **yazılmış, rotalı ve çalışır** olduğunu ama
/// `MainLayout.razor`'a hiç eklenmediğini buldu — yani kod vardı, kullanıcı ulaşamıyordu.
/// Hiçbir test bunu yakalamıyordu çünkü sayfalar teknik olarak sağlamdı.</para>
///
/// <para>Kapsam bilinçli olarak DAR: yalnız <c>/raporlar/*</c>. Sistem sayfaları (`/login`,
/// `/Error`, `/not-found`), platform konsolu (ayrı layout), parametrik detay rotaları ve alt-akış
/// sayfaları (`/web-sitesi/arac-ekle`) menüde OLMAMALI — onları kapsama sokmak testi gürültüye
/// boğardı. Rapor ekranlarında ise "menüde yoksa yok hükmünde".</para>
/// </summary>
public sealed class MenuKapsamaTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx")))
            d = d.Parent;
        Assert.NotNull(d);   // test bin'den yukarı çıkarak repo kökünü bulmalı
        return d!.FullName;
    }

    [Fact]
    public void Rapor_sayfalarinin_TAMAMI_menude()
    {
        var kok = RepoKok();
        var sayfalar = Path.Combine(kok, "src/RentACar.Web/Components/Pages");
        var menu = File.ReadAllText(Path.Combine(kok, "src/RentACar.Web/Components/Layout/MainLayout.razor"));

        var eksik = new List<string>();
        foreach (var dosya in Directory.EnumerateFiles(sayfalar, "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(dosya), @"@page\s+""(/raporlar[^""]*)"""))
            {
                var rota = m.Groups[1].Value;
                if (rota.Contains('{')) continue;                       // parametrik detay: drill-down'dan açılır
                if (!menu.Contains($"\"{rota}\"", StringComparison.Ordinal))
                    eksik.Add($"{rota}  ({Path.GetRelativePath(kok, dosya)})");
            }
        }

        Assert.True(eksik.Count == 0,
            "Bu rapor sayfaları yazılmış ama MainLayout menüsünde yok — kullanıcı ulaşamaz:\n  "
            + string.Join("\n  ", eksik));
    }
}
