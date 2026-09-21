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
    private const string ErrorSayfa = "src/RentACar.Web/Components/Pages/Error.razor";
    private const string NotFoundSayfa = "src/RentACar.Web/Components/Pages/NotFound.razor";

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Oku(string goreliYol) => File.ReadAllText(Path.Combine(RepoKok(), goreliYol));

    // =====================================================================
    // (1) .num — sağa yaslama
    // =====================================================================

    /// <summary>
    /// td/th'ye text-align veren ve <c>table td.num</c>'u (0,1,2) YENEBİLEN bilinçli kurallar.
    /// Buraya ekleme yapmadan önce: kural bir sayı hücresine denk gelebiliyor mu? Gelebiliyorsa
    /// seçiciye <c>:not(.num)</c> ekleyin, istisna yazmayın.
    /// </summary>
    private static readonly HashSet<string> BilincliIstisnalar = new(StringComparer.Ordinal)
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
        Assert.Equal((0, 1, 1), Ozgulluk(".grid td"));
        Assert.Equal((0, 1, 2), Ozgulluk("table td.num"));
        Assert.Equal((0, 2, 1), Ozgulluk(".cal td.veh"));
        Assert.Equal((0, 2, 1), Ozgulluk(".kart-mobil td[colspan]"));
        Assert.Equal((0, 2, 2), Ozgulluk(".a > tr td:first-child"));
        Assert.Equal((1, 1, 2), Ozgulluk(".a:not(.b, #c) td::before"));
        Assert.Equal((0, 1, 1), Ozgulluk(":where(.a .b) .c td"));
    }

    [Fact]
    public void Num_hucresi_ve_basligi_saga_yaslanir_rakamlar_esit_genislikte()
    {
        var kurallar = Kurallar(Oku(AppCss)).ToList();

        Assert.True(kurallar.Any(k => SagaYaslar(k.Govde) && Seciciler(k.Secici).Any(s => NumHucresi(s, "td"))),
            "app.css'te td.num'u sağa yaslayan kural yok — tutar kolonları sola yaslanır.");
        Assert.True(kurallar.Any(k => SagaYaslar(k.Govde) && Seciciler(k.Secici).Any(s => NumHucresi(s, "th"))),
            "app.css'te th.num'u sağa yaslayan kural yok — başlık rakamlarla aynı kenarda durmaz.");
        Assert.True(kurallar.Any(k =>
                Regex.IsMatch(k.Govde, @"font-variant-numeric\s*:\s*tabular-nums")
                && Seciciler(k.Secici).Any(s => Regex.IsMatch(Ozne(s), @"\.num(?![\w-])"))),
            ".num için tabular-nums yok — basamaklar alt alta gelmez, kolon gözle toplanamaz.");
    }

    [Fact]
    public void Num_saga_yaslamasini_sessizce_yenen_tablo_kurali_yok()
    {
        var css = Oku(AppCss);
        var kurallar = Kurallar(css).ToList();

        var numKural = kurallar.FirstOrDefault(k =>
            SagaYaslar(k.Govde) && Seciciler(k.Secici).Any(s => NumHucresi(s, "td")));
        Assert.NotNull(numKural);
        var numOzgulluk = Seciciler(numKural!.Secici).Where(s => NumHucresi(s, "td")).Select(Ozgulluk).Min();

        var tehditler = new List<string>();
        foreach (var k in kurallar)
        {
            var m = Regex.Match(k.Govde, @"text-align\s*:\s*([^;]+)");
            if (!m.Success) continue;
            var ham = m.Groups[1].Value.Trim();
            var onemli = ham.Contains("!important", StringComparison.Ordinal);
            var deger = ham.Replace("!important", "", StringComparison.Ordinal).Trim();
            if (deger is "right" or "end") continue;

            foreach (var s in Seciciler(k.Secici))
            {
                var ozne = Ozne(s);
                if (!Regex.IsMatch(ozne, @"^(td|th)(?![\w-])")) continue;
                // Sayı hücresini AÇIKÇA hedefleyen kural bilinçlidir (ör. td.num.merkez).
                if (Regex.IsMatch(ozne, @"\.num(?![\w-])")) continue;

                var fark = Ozgulluk(s).CompareTo(numOzgulluk);
                var yener = onemli || fark > 0 || (fark == 0 && k.Konum > numKural.Konum);
                if (yener && !BilincliIstisnalar.Contains(s))
                    tehditler.Add($"{s} {{ text-align: {ham} }}");
            }
        }

        Assert.True(tehditler.Count == 0,
            "Şu kurallar `table td.num { text-align: right }` kuralını yeniyor — sayı kolonları sessizce " +
            "sola/ortaya döner. Seçiciye `:not(.num)` ekleyin; gerçekten sayı hücresine denk gelmiyorsa " +
            "BilincliIstisnalar'a gerekçesiyle yazın:\n  " + string.Join("\n  ", tehditler));
    }

    // =====================================================================
    // (2) Hata sayfaları — Türkçe, yönlendirici, destek kodlu
    // =====================================================================

    [Fact]
    public void Blazor_hata_sinirinin_metni_Turkce()
    {
        var css = Oku(AppCss);
        Assert.DoesNotContain("An error has occurred", css, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"\.blazor-error-boundary::after\s*\{\s*content:\s*""[^""]*hata[^""]*""", css);
    }

    [Theory]
    [InlineData(ErrorSayfa)]
    [InlineData(NotFoundSayfa)]
    public void Hata_sayfasi_Ingilizce_sablon_metni_ve_gelistirici_tavsiyesi_icermez(string yol)
    {
        var gorunen = GorunenMetin(Oku(yol));

        // Proje şablonunun cümleleri (elle yazıldı — şablondan kopyalandı, kodla üretilmedi).
        string[] yasak =
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
        foreach (var ifade in yasak)
            Assert.False(gorunen.Contains(ifade, StringComparison.OrdinalIgnoreCase),
                $"{yol} kullanıcıya görünen kısımda şablon ifadesi taşıyor: \"{ifade}\"");
    }

    [Theory]
    [InlineData(ErrorSayfa, "/Error", "UseExceptionHandler(\"/Error\"")]
    [InlineData(NotFoundSayfa, "/not-found", "UseStatusCodePagesWithReExecute(\"/not-found\"")]
    public void Hata_sayfasi_boru_hattinin_bekledigi_yolda_ve_cikis_yolu_gosterir(
        string yol, string rota, string boruHatti)
    {
        var kaynak = Oku(yol);

        // Dosya BOM ile başlıyor; @page yine İLK yönerge kalmalı. Yol boru hattıyla aynı olmalı —
        // aksi halde hata anında yeniden çalıştırma 404'e düşer ve kullanıcı boş sayfa görür.
        Assert.StartsWith($"@page \"{rota}\"", kaynak.TrimStart('\uFEFF'), StringComparison.Ordinal);
        Assert.Contains(boruHatti, Oku("src/RentACar.Web/Program.cs"), StringComparison.Ordinal);

        var gorunen = GorunenMetin(kaynak);
        Assert.Contains("class=\"page-head\"", gorunen, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", gorunen, StringComparison.Ordinal);      // Panele dön
        Assert.Contains("data-rc-geri", gorunen, StringComparison.Ordinal);    // Geri dön (rc-ui.js)
        Assert.DoesNotMatch(@"href\s*=\s*""[#.]""", gorunen);                  // base href köke atar
    }

    [Fact]
    public void Hata_kutusu_koyu_temada_zemini_yuzeyden_turetir()
    {
        // Adversarial bulgu: .error zemini sabit açık pembe (#fdecea); koyu temada --danger açık kırmızı
        // (#f87171) olduğundan Error/NotFound'un ana cümlesi ~2,4:1 kontrastla okunmuyordu. Koyu tema
        // İKİ yoldan gelir (elle toggle + sistem tercihi) — ikisinde de .error zemini yeniden tanımlı olmalı.
        var css = Regex.Replace(Oku(AppCss), @"/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.Matches(new Regex(@":root\[data-theme=""dark""\]\s+\.error\s*\{[^}]*background\s*:\s*color-mix\("), css);
        Assert.Matches(new Regex(@"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root:not\(\[data-theme=""light""\]\)\s+\.error\s*\{[^}]*background\s*:\s*color-mix\("), css);
    }

    [Fact]
    public void NotFound_arama_kutusunu_yalniz_girisli_kullaniciya_onerir()
    {
        // Adversarial bulgu: sayfa [Authorize]'sız (girişsiz kullanıcıya da çıkar) ama girişsiz kabukta
        // (MainLayout NotAuthorized dalı yalnız @Body basar) arama kutusu YOK; metin "sol menüdeki arama
        // kutusu"nu öneriyordu — ki girişli kabukta da kutu sol menüde değil, üst çubukta.
        var gorunen = GorunenMetin(Oku(NotFoundSayfa));
        Assert.DoesNotContain("sol menü", gorunen, StringComparison.OrdinalIgnoreCase);
        var m = Regex.Match(gorunen, @"<Authorized>(?<ic>.*?)</Authorized>", RegexOptions.Singleline);
        Assert.True(m.Success, "Arama önerisi <AuthorizeView><Authorized> içinde olmalı.");
        Assert.Contains("Hızlı Arama", m.Groups["ic"].Value, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(gorunen, "Hızlı Arama"));
    }

    [Fact]
    public void Geri_don_dugmesini_isleyen_betik_yuklu()
    {
        // data-rc-geri yalnız bir işaret; betik kalkarsa "Geri dön" ölü düğme olur.
        Assert.Contains("[data-rc-geri]", Oku("src/RentACar.Web/wwwroot/js/rc-ui.js"), StringComparison.Ordinal);
        Assert.Contains("js/rc-ui.js", Oku("src/RentACar.Web/Components/App.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Error_sayfasi_destek_kodunu_loglardaki_request_id_ile_ayni_ifadeden_uretir_ve_anonime_acik()
    {
        var kaynak = Oku(ErrorSayfa);
        var yorumsuz = Regex.Replace(kaynak, @"@\*.*?\*@", "", RegexOptions.Singleline);

        // Kod ekranda görünmeli.
        Assert.Contains("@DestekKodu", GorunenMetin(kaynak), StringComparison.Ordinal);

        // Loglar request_id'yi Activity.Current.TraceId'den yazıyor; sayfa başka bir kimlik
        // gösterirse (şablondaki Activity.Current.Id gibi) kullanıcının ilettiği kod loglarda bulunmaz.
        const string ifade = "Activity.Current?.TraceId.ToString()";
        Assert.Contains(ifade, Oku("src/RentACar.Web/Observability/RequestEnrichment.cs"), StringComparison.Ordinal);
        Assert.Contains(ifade, yorumsuz, StringComparison.Ordinal);

        // Giriş ekranında da arıza olabilir: [Authorize] eklenirse kimliksiz kullanıcı hata yerine
        // giriş sayfasına atılır ve ne olduğunu hiç öğrenmez.
        Assert.DoesNotContain("[Authorize", yorumsuz, StringComparison.Ordinal);
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
    private static IEnumerable<Kural> Kurallar(string css)
    {
        var temiz = Regex.Replace(css, @"/\*.*?\*/", m => new string(' ', m.Length), RegexOptions.Singleline);
        foreach (Match m in Regex.Matches(temiz, @"([^{}]+)\{([^{}]*)\}"))
        {
            var secici = m.Groups[1].Value;
            var noktaliVirgul = secici.LastIndexOf(';');
            if (noktaliVirgul >= 0) secici = secici[(noktaliVirgul + 1)..];   // @charset "…"; artığı
            secici = Regex.Replace(secici, @"\s+", " ").Trim();
            if (secici.Length == 0 || secici.StartsWith('@')) continue;
            yield return new Kural(secici, m.Groups[2].Value, m.Index);
        }
    }

    private static bool SagaYaslar(string govde) => Regex.IsMatch(govde, @"text-align\s*:\s*right\b");

    private static bool NumHucresi(string secici, string etiket)
    {
        var ozne = Ozne(secici);
        return Regex.IsMatch(ozne, $@"^{etiket}(?![\w-])") && Regex.IsMatch(ozne, @"\.num(?![\w-])");
    }

    /// <summary>Virgülle ayrılmış seçici listesini üst düzeyde böler (<c>:not(a, b)</c> bölünmez).</summary>
    private static IEnumerable<string> Seciciler(string liste)
    {
        int derinlik = 0, bas = 0;
        for (var i = 0; i < liste.Length; i++)
        {
            var c = liste[i];
            if (c is '(' or '[') derinlik++;
            else if (c is ')' or ']') derinlik--;
            else if (c == ',' && derinlik == 0)
            {
                yield return liste[bas..i].Trim();
                bas = i + 1;
            }
        }
        yield return liste[bas..].Trim();
    }

    /// <summary>Seçicinin öznesi: son birleştiriciden (boşluk, &gt;, +, ~) sonraki bileşik.</summary>
    private static string Ozne(string secici)
    {
        var derinlik = 0;
        for (var i = secici.Length - 1; i >= 0; i--)
        {
            var c = secici[i];
            if (c is ')' or ']') derinlik++;
            else if (c is '(' or '[') derinlik--;
            else if (derinlik == 0 && c is ' ' or '>' or '+' or '~') return secici[(i + 1)..].Trim();
        }
        return secici.Trim();
    }

    /// <summary>
    /// CSS özgüllüğü (id, sınıf/öznitelik/sözde-sınıf, tip/sözde-öğe). <c>:not/:is/:has</c> en
    /// özgül argümanını, <c>:where</c> sıfırı sayar.
    /// </summary>
    private static (int, int, int) Ozgulluk(string secici)
    {
        int a = 0, b = 0, c = 0, i = 0;
        while (i < secici.Length)
        {
            var ch = secici[i];
            if (ch == '#') { a++; i = AdSonu(secici, i + 1); }
            else if (ch == '.') { b++; i = AdSonu(secici, i + 1); }
            else if (ch == '[')
            {
                b++;
                var kapanis = secici.IndexOf(']', i);
                i = kapanis < 0 ? secici.Length : kapanis + 1;   // bozuk seçicide sonsuz döngü olmasın
            }
            else if (ch == ':' && i + 1 < secici.Length && secici[i + 1] == ':')
            {
                c++;
                i = AdSonu(secici, i + 2);
            }
            else if (ch == ':')
            {
                var son = AdSonu(secici, i + 1);
                var ad = secici[(i + 1)..son].ToLowerInvariant();
                if (son < secici.Length && secici[son] == '(')
                {
                    var kapanis = EslesenParantez(secici, son);
                    var ic = secici[(son + 1)..Math.Min(kapanis, secici.Length)];
                    if (ad is "not" or "is" or "has")
                    {
                        var enYuksek = Seciciler(ic).Select(Ozgulluk).Max();
                        a += enYuksek.Item1; b += enYuksek.Item2; c += enYuksek.Item3;
                    }
                    else if (ad != "where") b++;   // :nth-child(…) vb.
                    i = kapanis + 1;
                }
                else
                {
                    // Eski tek-iki-noktalı sözde-öğeler tip ağırlığı taşır.
                    if (ad is "before" or "after" or "first-line" or "first-letter") c++; else b++;
                    i = son;
                }
            }
            else if (char.IsLetter(ch))
            {
                c++;
                i = AdSonu(secici, i);
            }
            else i++;   // birleştirici, boşluk, '*'
        }
        return (a, b, c);
    }

    private static int AdSonu(string s, int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '-' or '_')) i++;
        return i;
    }

    private static int EslesenParantez(string s, int acilis)
    {
        var derinlik = 0;
        for (var i = acilis; i < s.Length; i++)
        {
            if (s[i] == '(') derinlik++;
            else if (s[i] == ')' && --derinlik == 0) return i;
        }
        return s.Length;   // kapanmamış parantez: çağıran döngüyü bitirir
    }

    /// <summary>Razor yorumları ve <c>@code</c> bloğu atılmış hali — kullanıcının gördüğü işaretleme.</summary>
    private static string GorunenMetin(string razor)
    {
        var yorumsuz = Regex.Replace(razor, @"@\*.*?\*@", "", RegexOptions.Singleline);
        var kod = yorumsuz.IndexOf("@code", StringComparison.Ordinal);
        return kod >= 0 ? yorumsuz[..kod] : yorumsuz;
    }
}
