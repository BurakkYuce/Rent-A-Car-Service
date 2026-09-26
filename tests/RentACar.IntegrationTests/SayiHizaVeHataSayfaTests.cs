using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// İki kaynak-tarama çiti: (1) sayı/para kolonlarının sağa yaslanması, (2) hata sayfalarının Türkçe
/// ve kullanıcıya ne yapacağını söyler halde kalması.
///
/// <para><b>Neden var (1):</b> <c>class="num"</c> Razor'da 855 öğede (433 td, 375 th, 47 span)
/// kullanılıyordu ama HİÇBİR stil dosyasında tanımlı değildi — tüm tutar kolonları sola yaslı
/// görünüyordu ve kimse fark etmedi, çünkü eksik bir CSS kuralı hiçbir hata üretmez. Kural bir gün
/// silinirse ya da dosyanın altına eklenen daha güçlü bir tablo kuralı (<c>.x .grid td</c>) hizayı
/// sessizce geri sola çekerse bu test kırmızıya döner.</para>
///
/// <para><b>Neden var (2):</b> <c>/Error</c> ve <c>/not-found</c> proje şablonundan İngilizce
/// kalmıştı; <c>/Error</c> son kullanıcıya "Development ortamını açın" tavsiyesi veriyordu (üretimde
/// ayrıntılı hata sayfası = istisna ayrıntısı sızıntısı).</para>
/// </summary>
public sealed class SayiHizaVeHataSayfaTests
{
    private const string AppCss = "src/RentACar.Web/wwwroot/app.css";
    private const string ErrorPage = "src/RentACar.Web/Components/Pages/Error.razor";
    private const string NotFoundPage = "src/RentACar.Web/Components/Pages/NotFound.razor";

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    // =====================================================================
    // (1) .num — sağa yaslama
    // =====================================================================

    /// <summary>
    /// td/th'ye text-align veren ve <c>table td.num</c>'u (0,1,2) YENEBİLEN bilinçli kurallar.
    /// Buraya ekleme yapmadan önce: kural bir sayı hücresine denk gelebiliyor mu? Gelebiliyorsa
    /// seçiciye <c>:not(.num)</c> ekleyin, istisna yazmayın.
    /// </summary>
    private static readonly HashSet<string> DeliberateExceptions = new(StringComparer.Ordinal)
    {
        // Mobil kart görünümü: etiket solda, değer flex ile zaten sağda — text-align anlamsız.
        ".kart-mobil td",
        // "Kayıt yok" satırı (tek hücre, colspan) kartta ortalanır; sayı hücresi değil.
        ".kart-mobil td[colspan]",
        // Takvimin sabit araç kolonu (plaka) — asla sayı değil.
        ".cal th.veh",
        ".cal td.veh",
    };

    [Fact]
    public void Ozgulluk_hesabi_elle_hesaplanmis_degerlerle_uyusur()
    {
        // Çitin kendisi özgüllük hesabına dayanıyor; yanlış hesap çiti sessizce etkisiz bırakır.
        // Beklenen değerler CSS Selectors Level 4 kurallarıyla ELLE hesaplandı.
        Assert.Equal((0, 1, 1), Specificity(".grid td"));
        Assert.Equal((0, 1, 2), Specificity("table td.num"));
        Assert.Equal((0, 2, 1), Specificity(".cal td.veh"));
        Assert.Equal((0, 2, 1), Specificity(".kart-mobil td[colspan]"));
        Assert.Equal((0, 2, 2), Specificity(".a > tr td:first-child"));
        Assert.Equal((1, 1, 2), Specificity(".a:not(.b, #c) td::before"));
        Assert.Equal((0, 1, 1), Specificity(":where(.a .b) .c td"));
    }

    [Fact]
    public void Num_hucresi_ve_basligi_saga_yaslanir_rakamlar_esit_genislikte()
    {
        var rules = Rules(Read(AppCss)).ToList();

        Assert.True(rules.Any(k => AlignsRight(k.Govde) && Selectors(k.Secici).Any(s => NumCell(s, "td"))),
            "app.css'te td.num'u sağa yaslayan kural yok — tutar kolonları sola yaslanır.");
        Assert.True(rules.Any(k => AlignsRight(k.Govde) && Selectors(k.Secici).Any(s => NumCell(s, "th"))),
            "app.css'te th.num'u sağa yaslayan kural yok — başlık rakamlarla aynı kenarda durmaz.");
        Assert.True(rules.Any(k =>
                Regex.IsMatch(k.Govde, @"font-variant-numeric\s*:\s*tabular-nums")
                && Selectors(k.Secici).Any(s => Regex.IsMatch(Subject(s), @"\.num(?![\w-])"))),
            ".num için tabular-nums yok — basamaklar alt alta gelmez, kolon gözle toplanamaz.");
    }

    [Fact]
    public void Num_saga_yaslamasini_sessizce_yenen_tablo_kurali_yok()
    {
        var css = Read(AppCss);
        var rules = Rules(css).ToList();

        var numRule = rules.FirstOrDefault(k =>
            AlignsRight(k.Govde) && Selectors(k.Secici).Any(s => NumCell(s, "td")));
        Assert.NotNull(numRule);
        var numSpecificity = Selectors(numRule!.Secici).Where(s => NumCell(s, "td")).Select(Specificity).Min();

        var threats = new List<string>();
        foreach (var k in rules)
        {
            var m = Regex.Match(k.Govde, @"text-align\s*:\s*([^;]+)");
            if (!m.Success) continue;
            var raw = m.Groups[1].Value.Trim();
            var important = raw.Contains("!important", StringComparison.Ordinal);
            var value = raw.Replace("!important", "", StringComparison.Ordinal).Trim();
            if (value is "right" or "end") continue;

            foreach (var s in Selectors(k.Secici))
            {
                var subject = Subject(s);
                if (!Regex.IsMatch(subject, @"^(td|th)(?![\w-])")) continue;
                // Sayı hücresini AÇIKÇA hedefleyen kural bilinçlidir (ör. td.num.merkez).
                if (Regex.IsMatch(subject, @"\.num(?![\w-])")) continue;

                var difference = Specificity(s).CompareTo(numSpecificity);
                var yener = important || difference > 0 || (difference == 0 && k.Konum > numRule.Konum);
                if (yener && !DeliberateExceptions.Contains(s))
                    threats.Add($"{s} {{ text-align: {raw} }}");
            }
        }

        Assert.True(threats.Count == 0,
            "Şu kurallar `table td.num { text-align: right }` kuralını yeniyor — sayı kolonları sessizce " +
            "sola/ortaya döner. Seçiciye `:not(.num)` ekleyin; gerçekten sayı hücresine denk gelmiyorsa " +
            "BilincliIstisnalar'a gerekçesiyle yazın:\n  " + string.Join("\n  ", threats));
    }

    // =====================================================================
    // (2) Hata sayfaları — Türkçe, yönlendirici, destek kodlu
    // =====================================================================

    [Fact]
    public void Blazor_hata_sinirinin_metni_Turkce()
    {
        var css = Read(AppCss);
        Assert.DoesNotContain("An error has occurred", css, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"\.blazor-error-boundary::after\s*\{\s*content:\s*""[^""]*hata[^""]*""", css);
    }

    [Theory]
    [InlineData(ErrorPage)]
    [InlineData(NotFoundPage)]
    public void Hata_sayfasi_Ingilizce_sablon_metni_ve_gelistirici_tavsiyesi_icermez(string path)
    {
        var visible = VisibleText(Read(path));

        // Proje şablonunun cümleleri (elle yazıldı — şablondan kopyalandı, kodla üretilmedi).
        string[] ban =
        [
            "An error occurred",
            "while processing your request",
            "Development",
            "ASPNETCORE_ENVIRONMENT",
            "Request ID",
            "Sorry",
            "does not exist",
            "Not Found",
            "<PageTitle>Error</PageTitle>",
        ];
        foreach (var expression in ban)
            Assert.False(visible.Contains(expression, StringComparison.OrdinalIgnoreCase),
                $"{path} kullanıcıya görünen kısımda şablon ifadesi taşıyor: \"{expression}\"");
    }

    [Theory]
    [InlineData(ErrorPage, "/Error", "UseExceptionHandler(\"/Error\"")]
    [InlineData(NotFoundPage, "/not-found", "UseStatusCodePagesWithReExecute(\"/not-found\"")]
    public void Hata_sayfasi_boru_hattinin_bekledigi_yolda_ve_cikis_yolu_gosterir(
        string path, string route, string pipeline)
    {
        var source = Read(path);

        // Dosya BOM ile başlıyor; @page yine İLK yönerge kalmalı. Yol boru hattıyla aynı olmalı —
        // aksi halde hata anında yeniden çalıştırma 404'e düşer ve kullanıcı boş sayfa görür.
        Assert.StartsWith($"@page \"{route}\"", source.TrimStart('\uFEFF'), StringComparison.Ordinal);
        Assert.Contains(pipeline, Read("src/RentACar.Web/Program.cs"), StringComparison.Ordinal);

        var visible = VisibleText(source);
        Assert.Contains("class=\"page-head\"", visible, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", visible, StringComparison.Ordinal);      // Panele dön
        Assert.Contains("data-rc-geri", visible, StringComparison.Ordinal);    // Geri dön (rc-ui.js)
        Assert.DoesNotMatch(@"href\s*=\s*""[#.]""", visible);                  // base href köke atar
    }

    [Fact]
    public void Hata_kutusu_koyu_temada_zemini_yuzeyden_turetir()
    {
        // Adversarial bulgu: .error zemini sabit açık pembe (#fdecea); koyu temada --danger açık kırmızı
        // (#f87171) olduğundan Error/NotFound'un ana cümlesi ~2,4:1 kontrastla okunmuyordu. Koyu tema
        // İKİ yoldan gelir (elle toggle + sistem tercihi) — ikisinde de .error zemini yeniden tanımlı olmalı.
        var css = Regex.Replace(Read(AppCss), @"/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.Matches(new Regex(@":root\[data-theme=""dark""\]\s+\.error\s*\{[^}]*background\s*:\s*color-mix\("), css);
        Assert.Matches(new Regex(@"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root:not\(\[data-theme=""light""\]\)\s+\.error\s*\{[^}]*background\s*:\s*color-mix\("), css);
    }

    [Fact]
    public void NotFound_arama_kutusunu_yalniz_girisli_kullaniciya_onerir()
    {
        // Adversarial bulgu: sayfa [Authorize]'sız (girişsiz kullanıcıya da çıkar) ama girişsiz kabukta
        // (MainLayout NotAuthorized dalı yalnız @Body basar) arama kutusu YOK; metin "sol menüdeki arama
        // kutusu"nu öneriyordu — ki girişli kabukta da kutu sol menüde değil, üst çubukta.
        var visible = VisibleText(Read(NotFoundPage));
        Assert.DoesNotContain("sol menü", visible, StringComparison.OrdinalIgnoreCase);
        var m = Regex.Match(visible, @"<Authorized>(?<ic>.*?)</Authorized>", RegexOptions.Singleline);
        Assert.True(m.Success, "Arama önerisi <AuthorizeView><Authorized> içinde olmalı.");
        Assert.Contains("Hızlı Arama", m.Groups["ic"].Value, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(visible, "Hızlı Arama"));
    }

    [Fact]
    public void Geri_don_dugmesini_isleyen_betik_yuklu()
    {
        // data-rc-geri yalnız bir işaret; betik kalkarsa "Geri dön" ölü düğme olur.
        Assert.Contains("[data-rc-geri]", Read("src/RentACar.Web/wwwroot/js/rc-ui.js"), StringComparison.Ordinal);
        Assert.Contains("js/rc-ui.js", Read("src/RentACar.Web/Components/App.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Error_sayfasi_destek_kodunu_loglardaki_request_id_ile_ayni_ifadeden_uretir_ve_anonime_acik()
    {
        var source = Read(ErrorPage);
        var withoutComments = Regex.Replace(source, @"@\*.*?\*@", "", RegexOptions.Singleline);

        // Kod ekranda görünmeli.
        Assert.Contains("@SupportCode", VisibleText(source), StringComparison.Ordinal);

        // Loglar request_id'yi Activity.Current.TraceId'den yazıyor; sayfa başka bir kimlik
        // gösterirse (şablondaki Activity.Current.Id gibi) kullanıcının ilettiği kod loglarda bulunmaz.
        const string expression = "Activity.Current?.TraceId.ToString()";
        Assert.Contains(expression, Read("src/RentACar.Web/Observability/RequestEnrichment.cs"), StringComparison.Ordinal);
        Assert.Contains(expression, withoutComments, StringComparison.Ordinal);

        // Giriş ekranında da arıza olabilir: [Authorize] eklenirse kimliksiz kullanıcı hata yerine
        // giriş sayfasına atılır ve ne olduğunu hiç öğrenmez.
        Assert.DoesNotContain("[Authorize", withoutComments, StringComparison.Ordinal);
    }

    // =====================================================================
    // Yardımcılar — küçük CSS ayrıştırıcısı (yalnız app.css'in kullandığı sözdizimi için)
    // =====================================================================

    private sealed record Kural(string Secici, string Govde, int Konum);

    /// <summary>
    /// Düz kuralları sırasıyla döndürür. <c>@media</c> gibi blokların içindeki kurallar da gelir
    /// (desen iç içe süslü parantezi atlar, içteki düz kuralı yakalar); <c>@font-face</c> gibi
    /// at-kurallar atlanır. Yorumlar aynı uzunlukta boşlukla değiştirilir ki konumlar korunsun.
    /// </summary>
    private static IEnumerable<Kural> Rules(string css)
    {
        var clean = Regex.Replace(css, @"/\*.*?\*/", m => new string(' ', m.Length), RegexOptions.Singleline);
        foreach (Match m in Regex.Matches(clean, @"([^{}]+)\{([^{}]*)\}"))
        {
            var picker = m.Groups[1].Value;
            var semicolon = picker.LastIndexOf(';');
            if (semicolon >= 0) picker = picker[(semicolon + 1)..];   // @charset "…"; artığı
            picker = Regex.Replace(picker, @"\s+", " ").Trim();
            if (picker.Length == 0 || picker.StartsWith('@')) continue;
            yield return new Kural(picker, m.Groups[2].Value, m.Index);
        }
    }

    private static bool AlignsRight(string body) => Regex.IsMatch(body, @"text-align\s*:\s*right\b");

    private static bool NumCell(string picker, string label)
    {
        var subject = Subject(picker);
        return Regex.IsMatch(subject, $@"^{label}(?![\w-])") && Regex.IsMatch(subject, @"\.num(?![\w-])");
    }

    /// <summary>Virgülle ayrılmış seçici listesini üst düzeyde böler (<c>:not(a, b)</c> bölünmez).</summary>
    private static IEnumerable<string> Selectors(string list)
    {
        int depth = 0, start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            var c = list[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                yield return list[start..i].Trim();
                start = i + 1;
            }
        }
        yield return list[start..].Trim();
    }

    /// <summary>Seçicinin öznesi: son birleştiriciden (boşluk, &gt;, +, ~) sonraki bileşik.</summary>
    private static string Subject(string picker)
    {
        var depth = 0;
        for (var i = picker.Length - 1; i >= 0; i--)
        {
            var c = picker[i];
            if (c is ')' or ']') depth++;
            else if (c is '(' or '[') depth--;
            else if (depth == 0 && c is ' ' or '>' or '+' or '~') return picker[(i + 1)..].Trim();
        }
        return picker.Trim();
    }

    /// <summary>
    /// CSS özgüllüğü (id, sınıf/öznitelik/sözde-sınıf, tip/sözde-öğe). <c>:not/:is/:has</c> en
    /// özgül argümanını, <c>:where</c> sıfırı sayar.
    /// </summary>
    private static (int, int, int) Specificity(string picker)
    {
        int a = 0, b = 0, c = 0, i = 0;
        while (i < picker.Length)
        {
            var ch = picker[i];
            if (ch == '#') { a++; i = NameSuffix(picker, i + 1); }
            else if (ch == '.') { b++; i = NameSuffix(picker, i + 1); }
            else if (ch == '[')
            {
                b++;
                var closing = picker.IndexOf(']', i);
                i = closing < 0 ? picker.Length : closing + 1;   // bozuk seçicide sonsuz döngü olmasın
            }
            else if (ch == ':' && i + 1 < picker.Length && picker[i + 1] == ':')
            {
                c++;
                i = NameSuffix(picker, i + 2);
            }
            else if (ch == ':')
            {
                var last = NameSuffix(picker, i + 1);
                var name = picker[(i + 1)..last].ToLowerInvariant();
                if (last < picker.Length && picker[last] == '(')
                {
                    var closing = MatchingParenthesis(picker, last);
                    var ic = picker[(last + 1)..Math.Min(closing, picker.Length)];
                    if (name is "not" or "is" or "has")
                    {
                        var highest = Selectors(ic).Select(Specificity).Max();
                        a += highest.Item1; b += highest.Item2; c += highest.Item3;
                    }
                    else if (name != "where") b++;   // :nth-child(…) vb.
                    i = closing + 1;
                }
                else
                {
                    // Eski tek-iki-noktalı sözde-öğeler tip ağırlığı taşır.
                    if (name is "before" or "after" or "first-line" or "first-letter") c++; else b++;
                    i = last;
                }
            }
            else if (char.IsLetter(ch))
            {
                c++;
                i = NameSuffix(picker, i);
            }
            else i++;   // birleştirici, boşluk, '*'
        }
        return (a, b, c);
    }

    private static int NameSuffix(string s, int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '-' or '_')) i++;
        return i;
    }

    private static int MatchingParenthesis(string s, int opening)
    {
        var depth = 0;
        for (var i = opening; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return s.Length;   // kapanmamış parantez: çağıran döngüyü bitirir
    }

    /// <summary>Razor yorumları ve <c>@code</c> bloğu atılmış hali — kullanıcının gördüğü işaretleme.</summary>
    private static string VisibleText(string razor)
    {
        var withoutComments = Regex.Replace(razor, @"@\*.*?\*@", "", RegexOptions.Singleline);
        var code = withoutComments.IndexOf("@code", StringComparison.Ordinal);
        return code >= 0 ? withoutComments[..code] : withoutComments;
    }
}
