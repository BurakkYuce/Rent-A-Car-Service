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
        // F10.3: rapor öğeleri spa (/app/raporlar/…); Blazor sayfası menüdeki SPA öğesinin Blazor karşılığıyla eşlenir.
        var menu = MenuKaydi.Ogeler
            .SelectMany(o => new[] { o.Rota, RentACar.Web.Spa.IlkKesis.BlazorKarsiligi(o.Rota) })
            .OfType<string>().ToHashSet(StringComparer.Ordinal);

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

    /// <summary>
    /// F11.3: tanım ve sistem SPA rota dosyalarındaki her parametresiz ekran menüde spa öğesi olarak var. Genel tanım
    /// ekranları tek eşlemden (<c>definition-paths.ts</c> <c>DEFINITION_PATHS</c>) üretilir. Menü DIŞI bilinçli
    /// alt akışlar: ilan sihirbazının ilk adımı (<c>web-sitesi/arac-ekle</c>, İlanlar ekranından açılır), genel arama
    /// (<c>ara</c>, üst çubuk arama kutusu) ve kendi parolası (<c>profil/sifre-degistir</c>, kullanıcı menüsü).
    /// </summary>
    [Fact]
    public void F11_SPA_routes_are_all_in_menu()
    {
        var app = Path.Combine(RepoKok(), "src/RentACar.Frontend/src/app/features");
        var routes = File.ReadAllText(Path.Combine(app, "definitions/definitions.routes.ts"))
            + File.ReadAllText(Path.Combine(app, "system/system.routes.ts"));
        var definitionPaths = File.ReadAllText(Path.Combine(app, "definitions/definition-paths.ts"));
        var mapBlock = definitionPaths[definitionPaths.IndexOf("DEFINITION_PATHS", StringComparison.Ordinal)..];
        mapBlock = mapBlock[..mapBlock.IndexOf("};", StringComparison.Ordinal)];
        string[] outsideMenu = ["web-sitesi/arac-ekle", "ara", "profil/sifre-degistir"];
        var paths = Regex.Matches(routes, @"path: '(?<p>[a-z-/:]+)',").Select(m => m.Groups["p"].Value)
            .Concat(Regex.Matches(mapBlock, @"^\s*\w+: '(?<p>[a-z-]+)',", RegexOptions.Multiline).Select(m => m.Groups["p"].Value))
            .Where(p => !p.Contains(':') && !outsideMenu.Contains(p))
            .ToList();
        Assert.Equal(41, paths.Count); // 47 sayfa − 3 kimlikli − 3 menü dışı alt akış
        var menu = MenuKaydi.Ogeler.Where(o => o.Sahip == MenuKaydi.Spa).Select(o => o.Rota).ToHashSet(StringComparer.Ordinal);
        var missing = paths.Where(p => !menu.Contains("/app/" + p)).ToList();
        Assert.True(missing.Count == 0, "Menüde spa öğesi olmayan F11 ekranı: " + string.Join(", ", missing));
    }

    /// <summary>
    /// F10.3: SPA'nın rapor rota tablosundaki (<c>reports.routes.ts</c>) her kimliksiz rapor menüde spa öğesi olarak
    /// var — Blazor sayfaları silindiğinde (pilot sonrası) yukarıdaki çit boşa düşse de kapsama korunur.
    /// </summary>
    [Fact]
    public void SPA_rapor_rotalarinin_TAMAMI_menude()
    {
        var tablo = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Frontend/src/app/features/reports/reports.routes.ts"));
        var kodlar = Regex.Matches(tablo, @"^\s*\['(?<kod>[a-z-]+)',", RegexOptions.Multiline)
            .Select(m => m.Groups["kod"].Value).Where(k => k != "arac-karne").ToList();
        Assert.Equal(25, kodlar.Count);
        var menu = MenuKaydi.Ogeler.Where(o => o.Sahip == MenuKaydi.Spa).Select(o => o.Rota).ToHashSet(StringComparer.Ordinal);
        Assert.All(kodlar, k => Assert.Contains("/app/raporlar/" + k, menu));
    }
}
