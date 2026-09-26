namespace RentACar.IntegrationTests;

/// <summary>
/// Kaçak yönlendirme çiti: kullanıcıyı bulunduğu sayfadan KÖKE (<c>/</c>) atan üç desen
/// bir daha koda giremesin.
///
/// <para><b>Neden var (canlı şikayet, 2026-08-26):</b> "yeni kira oluştururken bazen durduk
/// yere ana ekrana atıyor". Üç bağımsız kök neden bulundu, ikisi bu testin kapsamında:</para>
///
/// <list type="number">
/// <item><b>meta-refresh.</b> <c>Home.razor</c> (120 sn) ve <c>FleetStatus.razor</c> (60 sn)
/// <c>&lt;meta http-equiv="refresh"&gt;</c> kullanıyordu. Blazor'ın enhanced navigation'ı GERÇEK
/// belge navigasyonu yapmadığı için (pushState), başka sayfaya geçildiğinde tarayıcının bekleyen
/// refresh'i İPTAL OLMUYOR ve kullanıcıyı terk ettiği sayfaya geri fırlatıyordu — kira formunun
/// ortasında, girilen tüm veriyle birlikte. Yerine <c>rc-tazele.js</c> + <c>data-rc-tazele</c>:
/// zamanlayıcı JS'te tutulur ve <c>enhancedload</c>'da iptal edilir.</item>
///
/// <item><b>Göreli/boş href.</b> <c>App.razor</c>'da <c>&lt;base href="/"&gt;</c> var; bu yüzden
/// <c>href="."</c> ve <c>href="#"</c> KÖKE çözülüyor. İkisi de canlıda vardı: hata bandındaki
/// "Yenile" (<c>MainLayout.razor</c>) ve kira formundaki "Yazdır" (<c>KiraForm.razor</c>).
/// Yazdır'ı yalnız JS'in <c>preventDefault</c>'u koruyordu — JS yüklenmeden tıklama, orta-tık
/// ve Ctrl+tık korumasızdı.</item>
/// </list>
///
/// <para>Üçüncü kök neden (yetkisiz POST → 403 → <c>/login</c> → girişli kullanıcı → <c>/</c>)
/// ayrı ele alınır: <c>AccessDeniedPath</c> artık <c>/yetkisiz</c>'e gider.</para>
///
/// <para><b>Kapsam:</b> yalnız <c>RentACar.Web</c> razor'ları. PublicSite ayrı bir uygulama ve
/// enhanced navigation kullanmıyor.</para>
/// </summary>
public sealed class KacakYonlendirmeTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx")))
            d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<string> WebRazors(string root)
        => Directory.EnumerateFiles(
            Path.Combine(root, "src/RentACar.Web/Components"), "*.razor", SearchOption.AllDirectories);

    [Fact]
    public void Meta_refresh_kullanilmaz()
    {
        var root = RepoRoot();
        var found = WebRazors(root)
            .Where(f => File.ReadAllText(f).Contains("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(found.Count == 0,
            "meta-refresh enhanced navigation'da İPTAL OLMAZ: kullanıcı başka sayfaya geçse bile " +
            "zamanlayıcı çalışır ve onu terk ettiği sayfaya geri fırlatır (kira formunda veri kaybı). " +
            "Periyodik tazeleme için `data-rc-tazele=\"<saniye>\"` kullan (rc-tazele.js).\n  " +
            string.Join("\n  ", found));
    }

    [Fact]
    public void Koke_cozulen_gorece_href_kullanilmaz()
    {
        var root = RepoRoot();
        // `<base href="/">` altında bu üç hedef de belge base'ine, yani KÖKE çözülür.
        string[] ban = ["href=\".\"", "href=\"./\"", "href=\"#\""];

        var found = new List<string>();
        foreach (var file in WebRazors(root))
        {
            var text = File.ReadAllText(file);
            foreach (var y in ban)
                if (text.Contains(y, StringComparison.Ordinal))
                    found.Add($"{Path.GetRelativePath(root, file)}  →  {y}");
        }

        Assert.True(found.Count == 0,
            "App.razor'da `<base href=\"/\">` var: \".\" ve \"#\" KÖKE çözülür ve kullanıcıyı ana " +
            "ekrana atar. Sayfayı yenilemek için `<button data-rc-reload>`, aksiyon bağlantısı için " +
            "gerçek hedef URL kullan.\n  " + string.Join("\n  ", found));
    }

    // F13.1a: "data-rc-tazele kullanılıyorsa script yüklü" testi silindi — tazeleme özniteliğini kullanan Blazor
    // sayfaları (Panel, Filo durumu) kalktı; yeni arayüz tazelemeyi kendi veri katmanında yapar.
}
