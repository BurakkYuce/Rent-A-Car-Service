namespace RentACar.IntegrationTests;

/// <summary>
/// Uygulama kabuğu (app-shell) çiti.
///
/// <para><b>Neden var (canlı şikayet):</b> "ana sayfada tüm menü ekrana sığarken aşağıya doğru
/// scroll var, taban görünüyor". Sebep footer DEĞİLDİ (uygulamada hiç <c>&lt;footer&gt;</c> yok):
/// <c>.app</c> grid satırı içerik yüksekliğine uzuyor, <c>.sidebar</c> ise <c>height:100vh</c>'de
/// kalıyordu → sol kolonun altında arka plan renginde boş bir şerit oluşuyordu. Ayrıca mobilde
/// <c>100vh</c> görünür alandan büyük olduğu için (URL çubuğu) içerik sığsa bile scroll çıkıyordu.</para>
///
/// <para>Çözüm klasik app-shell: <c>.app</c> tam ekran + <c>overflow:hidden</c>, scroll yalnız
/// <c>.main</c>'de. Bu testler o kararı sabitler.</para>
/// </summary>
public sealed class KabukTests
{
    private static string Css()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d!.FullName, "src/RentACar.Web/wwwroot/app.css"));
    }

    /// <summary>
    /// Seçiciyi SATIR BAŞINDA arar. Düz <c>IndexOf(".main {")</c> kullanılsaydı <c>@media print</c>
    /// içindeki <c>.app, .main { … }</c> istisnasını yakalar ve yanlış kuralı incelerdi.
    /// </summary>
    private static string Kural(string css, string secici)
    {
        var i = css.IndexOf("\n" + secici + " {", StringComparison.Ordinal);
        Assert.True(i >= 0, $"`{secici}` kuralı app.css'te (satır başında) bulunamadı.");
        var son = css.IndexOf('}', i);
        return css[i..son];
    }

    [Fact]
    public void Iskelet_vh_yerine_svh_kullanir()
    {
        // `100vh` mobil tarayıcıda URL çubuğu gizliyken ölçülen yüksekliktir — görünür alandan
        // ~60-100px BÜYÜK. İskelette kullanılırsa içerik sığsa bile scroll çubuğu belirir.
        var app = Kural(Css(), ".app");
        Assert.DoesNotContain("100vh", app, StringComparison.Ordinal);
        Assert.Contains("svh", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Scroll_konteyneri_main_sayfa_govdesi_degil()
    {
        var css = Css();
        Assert.Contains("overflow: hidden", Kural(css, ".app"), StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", Kural(css, ".main"), StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_sabit_yukseklikte_degil()
    {
        // `height:100vh` + sticky, grid satırı içerikle uzayınca sol kolonun altında boş şerit
        // ("taban") bırakıyordu. Sidebar artık kolonun TAMAMINI kaplar.
        var sb = Kural(Css(), ".sidebar");
        Assert.DoesNotContain("100vh", sb, StringComparison.Ordinal);
        Assert.DoesNotContain("position: sticky", sb, StringComparison.Ordinal);
    }

    [Fact]
    public void Yazdirmada_app_shell_cozulur()
    {
        // EN KRİTİK: bu istisna olmadan `.app{overflow:hidden;height:100svh}` yazdırmada yalnız
        // görünür alanı bastırır → çok sayfalı sözleşme/fatura TEK SAYFAYA KIRPILIR.
        var css = Css();
        var i = css.IndexOf("@media print", StringComparison.Ordinal);
        Assert.True(i >= 0, "app.css'te @media print bloğu yok.");
        var blok = css[i..css.IndexOf("\n}", i, StringComparison.Ordinal)];

        Assert.Contains(".app", blok, StringComparison.Ordinal);
        Assert.Contains(".main", blok, StringComparison.Ordinal);
        Assert.Contains("height: auto !important", blok, StringComparison.Ordinal);
        Assert.Contains("overflow: visible !important", blok, StringComparison.Ordinal);
    }
}
