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
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx")))
            d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<string> WebRazorlari(string kok)
        => Directory.EnumerateFiles(
            Path.Combine(kok, "src/RentACar.Web/Components"), "*.razor", SearchOption.AllDirectories);

    [Fact]
    public void Meta_refresh_kullanilmaz()
    {
        var kok = RepoKok();
        var bulunan = WebRazorlari(kok)
            .Where(f => File.ReadAllText(f).Contains("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(kok, f))
            .ToList();

        Assert.True(bulunan.Count == 0,
            "meta-refresh enhanced navigation'da İPTAL OLMAZ: kullanıcı başka sayfaya geçse bile " +
            "zamanlayıcı çalışır ve onu terk ettiği sayfaya geri fırlatır (kira formunda veri kaybı). " +
            "Periyodik tazeleme için `data-rc-tazele=\"<saniye>\"` kullan (rc-tazele.js).\n  " +
            string.Join("\n  ", bulunan));
    }

    [Fact]
    public void Koke_cozulen_gorece_href_kullanilmaz()
    {
        var kok = RepoKok();
        // `<base href="/">` altında bu üç hedef de belge base'ine, yani KÖKE çözülür.
        string[] yasak = ["href=\".\"", "href=\"./\"", "href=\"#\""];

        var bulunan = new List<string>();
        foreach (var dosya in WebRazorlari(kok))
        {
            var metin = File.ReadAllText(dosya);
            foreach (var y in yasak)
                if (metin.Contains(y, StringComparison.Ordinal))
                    bulunan.Add($"{Path.GetRelativePath(kok, dosya)}  →  {y}");
        }

        Assert.True(bulunan.Count == 0,
            "App.razor'da `<base href=\"/\">` var: \".\" ve \"#\" KÖKE çözülür ve kullanıcıyı ana " +
            "ekrana atar. Sayfayı yenilemek için `<button data-rc-reload>`, aksiyon bağlantısı için " +
            "gerçek hedef URL kullan.\n  " + string.Join("\n  ", bulunan));
    }

    [Fact]
    public void Tazeleme_ozniteligi_kullaniliyorsa_script_yuklu()
    {
        var kok = RepoKok();
        var kullanan = WebRazorlari(kok)
            .Where(f => File.ReadAllText(f).Contains("data-rc-tazele", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(kok, f))
            .ToList();

        // Öznitelik kullanılıyorsa onu okuyan script yüklenmiş olmalı; yoksa tazeleme SESSİZCE ölür.
        Assert.NotEmpty(kullanan);   // Home + FleetStatus: meta-refresh'ten buraya taşındılar
        var app = File.ReadAllText(Path.Combine(kok, "src/RentACar.Web/Components/App.razor"));
        Assert.Contains("js/rc-tazele.js", app, StringComparison.Ordinal);
    }
}
