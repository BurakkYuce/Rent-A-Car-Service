using System.Text.RegularExpressions;
using RentACar.Web.Api.Menu;

namespace RentACar.IntegrationTests;

/// <summary>
/// Menü kapsama çiti: yazılmış ama menüye eklenmemiş sayfa kalmasın.
///
/// <para><b>Neden var:</b> 2026-08-06 parite taraması `/raporlar/kasa-banka` ve
/// `/raporlar/servis-ozet` rotalarının **yazılmış, rotalı ve çalışır** olduğunu ama
/// `MainLayout.razor`'a hiç eklenmediğini buldu — yani kod vardı, kullanıcı ulaşamıyordu.
/// Hiçbir test bunu yakalamıyordu çünkü sayfalar teknik olarak sağlamdı.</para>
///
/// <para><b>F4.6:</b> menü artık KAYITTAN çizilir (<see cref="MenuKaydi"/>, iki arayüzün tek kaynağı) — kapsama
/// MainLayout metnine değil kayda bakar.</para>
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
        var menu = MenuKaydi.Ogeler.Select(o => o.Rota).ToHashSet(StringComparer.Ordinal);

        var eksik = new List<string>();
        foreach (var dosya in Directory.EnumerateFiles(sayfalar, "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(dosya), @"@page\s+""(/raporlar[^""]*)"""))
            {
                var rota = m.Groups[1].Value;
                if (rota.Contains('{')) continue;                       // parametrik detay: drill-down'dan açılır
                if (!menu.Contains(rota))
                    eksik.Add($"{rota}  ({Path.GetRelativePath(kok, dosya)})");
            }
        }

        Assert.True(eksik.Count == 0,
            "Bu rapor sayfaları yazılmış ama menü kaydında (MenuKaydi) yok — kullanıcı ulaşamaz:\n  "
            + string.Join("\n  ", eksik));
    }

    /// <summary>
    /// F7.3: cari/CRM SPA rota dosyalarındaki her parametresiz liste ekranı menüde spa öğesi olarak var (yeni cari
    /// kartı liste düğmesinden açılır; <c>:id</c>'li rotalar drill-down). Blazor sayfaları silindiğinde (pilot sonrası)
    /// kapsama SPA tarafında korunur.
    /// </summary>
    [Fact]
    public void F7_SPA_list_routes_are_all_in_menu()
    {
        var app = Path.Combine(RepoKok(), "src/RentACar.Frontend/src/app/features");
        var text = File.ReadAllText(Path.Combine(app, "customers/customers.routes.ts"))
            + File.ReadAllText(Path.Combine(app, "crm/crm.routes.ts"));
        var paths = Regex.Matches(text, @"path: '(?<p>[a-z-/:]+)',")
            .Select(m => m.Groups["p"].Value)
            .Where(p => !p.Contains(':') && p != "cariler/yeni")
            .ToList();
        Assert.Equal(new[] { "anketler", "assistans", "cariler", "crm", "hukuk", "sikayetler" }, paths.OrderBy(p => p, StringComparer.Ordinal));
        var menu = MenuKaydi.Ogeler.Where(o => o.Sahip == MenuKaydi.Spa).Select(o => o.Rota).ToHashSet(StringComparer.Ordinal);
        Assert.All(paths, p => Assert.Contains("/app/" + p, menu));
    }
}
