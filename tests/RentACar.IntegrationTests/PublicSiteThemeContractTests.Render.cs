using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Branches;
using RentACar.Application.SiteIcerik;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

public sealed partial class PublicSiteThemeContractTests
{
    // ---- Kontrast: css'ten değer çözümü + bağımsız WCAG hesabı ----

    [GeneratedRegex(@"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{([^}]*)\}\s*\}")]
    private static partial Regex DarkRoot();

    [GeneratedRegex(@":root\s*\{([^}]*)\}")]
    private static partial Regex RootBlock();

    [GeneratedRegex(@"(--[\w-]+)\s*:\s*([^;]+);")]
    private static partial Regex Declaration();

    private static Dictionary<string, string> Tokens(string css, bool dark)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var darkBlock = DarkRoot().Match(css);
        var light = darkBlock.Success ? css.Remove(darkBlock.Index, darkBlock.Length) : css;
        foreach (Match root in RootBlock().Matches(light))
            foreach (Match d in Declaration().Matches(root.Groups[1].Value)) map[d.Groups[1].Value] = d.Groups[2].Value.Trim();
        if (dark && darkBlock.Success)
            foreach (Match d in Declaration().Matches(darkBlock.Groups[1].Value)) map[d.Groups[1].Value] = d.Groups[2].Value.Trim();
        return map;
    }

    /// <summary>Token'ı hex'e çözer: <c>var(--x)</c> zinciri ve <c>light-dark(a, b)</c> — ikincisinde seçim
    /// <paramref name="darkScheme"/> ile (palet bloğunun o şemaya yazdığı color-scheme; css hangi setin basıldığını
    /// bilemez, koyu kimlikli setin açık şeması da koyudur). light-dark görülür ama şema verilmezse test düşer.</summary>
    private static string ResolveHex(Dictionary<string, string> map, string token, bool? darkScheme = null)
    {
        var v = map[token];
        for (var i = 0; i < 10; i++)
        {
            if (v.StartsWith("light-dark(", StringComparison.Ordinal))
            {
                Assert.True(darkScheme is not null, $"{token}: light-dark() şema bilgisi olmadan çözülemez");
                var args = v["light-dark(".Length..v.LastIndexOf(')')];
                var comma = args.IndexOf("),", StringComparison.Ordinal) + 1;
                v = (darkScheme!.Value ? args[(comma + 1)..] : args[..comma]).Trim();
            }
            else if (v.StartsWith("var(", StringComparison.Ordinal))
                v = map[v[4..v.IndexOf(')')].Trim()];
            else break;
        }
        Assert.Matches("^#[0-9a-fA-F]{6}$", v);
        return v;
    }

    [Fact]
    public void Cozumleyici_light_dark_secimini_semaya_gore_yapar()
    {
        var map = Tokens(":root{--p-a:#111111;--p-b:#eeeeee;}:root{--x:light-dark(var(--p-a), var(--p-b));--y:var(--x);}", dark: false);
        Assert.Equal("#111111", ResolveHex(map, "--y", darkScheme: false));
        Assert.Equal("#eeeeee", ResolveHex(map, "--y", darkScheme: true));
    }

    private static double Lum(string hex)
    {
        double Ch(int i)
        {
            var c = int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(1) + 0.7152 * Ch(3) + 0.0722 * Ch(5);
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Lum(a), Lum(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>
    /// Tema css'indeki palet token'ları YEDEKTİR (palet bloğu basılamazsa): değerleri temanın varsayılan setiyle aynı
    /// olmalı — yoksa blok yokken site başka renkte görünür. Vurgu yalnız set kendi vurgusunu taşıyorsa karşılaştırılır
    /// (vitrin: vurgu ana rengi izler, css de <c>var(--brand)</c>).
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Tema_css_yedekleri_varsayilan_setle_ayni(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var css = ReadCss(ThemeFile(theme));
        var palette = theme.DefaultPalette;
        foreach (var (dark, scheme) in new[] { (false, palette.Light), (true, palette.Dark) })
        {
            var map = Tokens(css, dark);
            var mode = dark ? "koyu" : "açık";
            var expected = new Dictionary<string, string?>
            {
                ["--bg"] = scheme.Bg, ["--bg-alt"] = scheme.BgAlt, ["--surface"] = scheme.Surface,
                ["--surface-raised"] = scheme.SurfaceRaised, ["--ink"] = scheme.Ink, ["--ink-muted"] = scheme.InkMuted,
                ["--ink-subtle"] = scheme.InkSubtle, ["--line"] = scheme.Line, ["--line-strong"] = scheme.LineStrong,
                ["--accent"] = scheme.Accent,
            };
            foreach (var (token, value) in expected)
            {
                if (value is null) continue;
                Assert.True(value == ResolveHex(map, token), $"{key} {mode}: {token} yedeği {ResolveHex(map, token)}, set {value}");
            }
        }
        var light = Tokens(css, dark: false);
        Assert.Equal(
            (palette.Light.Bg, palette.Light.Ink, palette.Dark.Bg, palette.Dark.Ink),
            (ResolveHex(light, "--day-bg"), ResolveHex(light, "--day-ink"), ResolveHex(light, "--night-bg"),
                ResolveHex(light, "--night-ink")));
    }

    /// <summary>Durum renkleri (hata/başarı) palete bağlı değil (gerekçeli muafiyet): her setin her şemasında zemin ve
    /// kart üstünde metin olarak AA kalmalı.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Durum_renkleri_her_setin_zemininde_AA(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var css = ReadCss(ThemeFile(theme));
        foreach (var palette in theme.Palettes)
        {
            var t = SitePaletteCss.Tokens(palette, null);
            foreach (var (dark, scheme) in new[] { (false, t.Light), (true, t.Dark) })
            {
                var map = Tokens(css, dark);
                // Şemanın koyuluğu bağımsız hesapla (zemin mürekkepten koyu mu) — light-dark() seçimini belirler.
                var darkScheme = Lum(scheme.Bg.Hex) < Lum(scheme.Ink.Hex);
                foreach (var status in new[] { "--danger", "--ok" })
                foreach (var ground in new[] { scheme.Bg, scheme.Surface })
                {
                    var c = Contrast(ResolveHex(map, status, darkScheme), ground.Hex);
                    Assert.True(c >= 4.5, $"{key}/{palette.Key} {(dark ? "koyu" : "açık")}: {status}/{ground.Hex} = {c:0.00}");
                }
            }
        }
    }

    [GeneratedRegex(@"color-scheme\s*:\s*(light|dark)\s*;")]
    private static partial Regex ColorSchemeDecl();

    /// <summary>
    /// Koyu kimlik desteği (Vitrin: DarkInLightMode, bloktaki color-scheme, koyu kimlikte dolgu üstü yazı adayı) AÇIK
    /// kimlikli temaların çıktısını değiştirmez. Pazar ve Rota'nın her setinde:
    /// (1) palet bloğunun yazdığı color-scheme, temanın KENDİ css bildirimiyle aynı (açık :root light, koyu blok dark) —
    ///     ek bildirim görünümü değiştirmez;
    /// (2) dolgu üstü yazı ve vurgu üstü yazı, iki şemada da AÇIK ŞEMANIN MÜREKKEBİ adayıyla seçilir (değişiklikten
    ///     önceki kural). Beklenen, kuralın testteki bağımsız uygulaması (WCAG hesabı üretim kodunu çağırmaz).
    /// Ayrıca varsayılan setlerin elle yazılmış değerleri SitePaletteCssTests.Varsayilan_setler_bugunku_renkleri_korur'da.
    /// </summary>
    [Theory]
    [InlineData("anahtar")]
    [InlineData("kiyas")]
    [InlineData("paket")]
    [InlineData("pazar")]
    [InlineData("rota")]
    public void Koyu_kimlik_destegi_acik_kimlikli_temalarin_ciktisini_degistirmez(string key)
    {
        var theme = SiteThemes.Resolve(key);
        var css = ReadCss(ThemeFile(theme));
        var darkMatch = DarkRoot().Match(css);
        var cssLight = ColorSchemeDecl().Matches(darkMatch.Success ? css.Remove(darkMatch.Index, darkMatch.Length) : css)
            .Select(m => m.Groups[1].Value).Distinct().Single();
        var cssDark = ColorSchemeDecl().Match(darkMatch.Value).Groups[1].Value;
        Assert.Equal(("light", "dark"), (cssLight, cssDark));

        static string OnFill(string fill, string darkInk)
        {
            var white = Contrast("#ffffff", fill);
            var ink = Contrast(darkInk, fill);
            if (white >= 4.5 && white >= ink) return "#ffffff";
            if (ink >= 4.5) return darkInk;
            return white >= Contrast("#000000", fill) ? "#ffffff" : "#000000";
        }

        foreach (var palette in theme.Palettes)
        {
            Assert.False(palette.DarkInLightMode, $"{key}/{palette.Key}");
            var t = SitePaletteCss.Tokens(palette, null);
            var block = SitePaletteCss.Css(t);
            var split = block.IndexOf("@media", StringComparison.Ordinal);
            Assert.Contains($";color-scheme:{cssLight}}}", block[..split]);
            Assert.EndsWith($";color-scheme:{cssDark}}}}}", block[split..]);

            var darkInk = t.Light.Ink.Hex; // değişiklikten önceki kural: iki şemada da açık şemanın mürekkebi
            foreach (var (s, colors, mode) in new[] { (t.Light, palette.Light, "açık"), (t.Dark, palette.Dark, "koyu") })
            {
                Assert.True(OnFill(s.Brand.Hex, darkInk) == s.OnBrand.Hex, $"{key}/{palette.Key} {mode}: on-brand {s.OnBrand.Hex}");
                if (colors.Accent is null) continue;
                var expectedAccentInk = colors.AccentInk is { } given && Contrast(given, s.Accent.Hex) >= 4.5
                    ? given : OnFill(s.Accent.Hex, darkInk);
                Assert.True(expectedAccentInk == s.AccentInk.Hex, $"{key}/{palette.Key} {mode}: accent-ink {s.AccentInk.Hex}");
            }
        }
    }

    // ---- Canlı boru hattı: 3 tema × sayfalar ----

    private async Task<Guid> SeedTenantAsync(string? theme, string? palette = null, string? color = null)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "tm" + Guid.NewGuid().ToString("N")[..10], Name = "Tema Deneme", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        if (theme is not null || palette is not null || color is not null)
        {
            // Ayar satırı RLS'li: firmanın kendi bağlamında, gerçek servisle yazılır.
            using var host = new TestHost(fx.AppConnectionString);
            using var s = host.ScopeFor(t.Id);
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await svc.GetAsync();
            m.SiteTemasi = theme;
            m.SitePaleti = palette;
            m.SiteVurguRengi = color;
            await svc.SaveAsync(m);
        }
        return t.Id;
    }

    public static TheoryData<string> ThemeKeys() => [.. SiteThemes.All.Select(t => t.Key)];

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    /// <summary>Yayında bir ilan (Fiat Egea 2023, günlük 1500 TL KDV dahil, haftalık toplam 9.100 TL, bir
    /// fotoğraf), altı yayında SSS ("Soru 1?" … "Soru 6?") ve bir aktif şube. Slug döner.</summary>
    private async Task<string> SeedContentAsync(Guid t)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(t);
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput
        {
            Kod = "TMA", Ad = "Tema Merkez", Il = "İzmir", Ilce = "Konak", Aktif = true,
        });
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "35TM" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
            ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        });
        await sp.GetRequiredService<VehiclePhotoService>().AddAsync(vehicleId, TinyPng);
        var listings = sp.GetRequiredService<WebListingService>();
        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Count == 1).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, 1500m, 9100m, null, true);
        await listings.StepThreeAsync(listingId, [new OzellikSatiri("Vites", "Manuel")]);
        var content = sp.GetRequiredService<SiteContentService>();
        for (var i = 1; i <= 6; i++)
            await content.SaveFaqAsync(new SssInput(null, $"Soru {i}?", $"Cevap {i}.", i, true));
        return (await listings.GetAsync(listingId))!.Ilan.Slug;
    }

    /// <summary>Sorgu tarihi: bugünden göreli (sabit tarih YOK — TestTarihBombasi), ISO gün.</summary>
    private static string Day(int offset)
        => DateTime.UtcNow.Date.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlock();

    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public async Task Her_tema_sayfalari_basar_satir_ici_betik_ve_dis_kaynak_yok(string key)
    {
        var tenant = await SeedTenantAsync(key);
        var slug = await SeedContentAsync(tenant);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        var theme = SiteThemes.Resolve(key);
        var dated = $"/musaitlik?bas={Day(30)}&bit={Day(33)}";

        foreach (var (path, status) in new[]
                 {
                     ("/", HttpStatusCode.OK), ("/musaitlik", HttpStatusCode.OK), (dated, HttpStatusCode.OK),
                     ($"/araclar/{slug}", HttpStatusCode.OK), ("/iletisim", HttpStatusCode.OK),
                     ("/araclar/olmayan-ilan", HttpStatusCode.NotFound), ("/yok/boyle/bir/adres", HttpStatusCode.NotFound),
                 })
        {
            using var r = await client.GetAsync(path);
            var html = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == status, $"{key} {path} → {(int)r.StatusCode}");
            Assert.True(r.Headers.Contains("Content-Security-Policy"), $"{key} {path}: CSP yok");
            // Kayıtlı temanın css'i yüklendi (parmak izli ad: tema-<ad>.<hash>.css) ve kabuk kanonik anahtarı taşır.
            Assert.Matches($@"css/{Path.GetFileNameWithoutExtension(theme.CssPath)}\.[a-z0-9]+\.css", html);
            Assert.Contains($"data-tema=\"{key}\"", html);
            Assert.Contains("class=\"page", html); // kabuk basıldı
            foreach (Match tag in Regex.Matches(html, "<script[^>]*>"))
            {
                var t = WebUtility.HtmlDecode(tag.Value); // bazı JSON-LD etiketlerinde `+` &#x2B; olarak kodlu
                Assert.True(t.Contains("src=", StringComparison.Ordinal)
                            || t.Contains("application/ld+json", StringComparison.Ordinal), $"{key} {path}: satır içi betik {tag.Value}");
            }
            // Dış KAYNAK yok (CSP 'self'): betik/görsel src'si ve stil/önyükleme/ikon href'i başka kökene gidemez.
            // (Kanonik <link> ve wa.me/harita <a> bağlantıları kaynak değil — kapsam dışı.)
            foreach (Match m in Regex.Matches(html,
                         @"<(?:script|img)[^>]*\ssrc=""(?:https?:)?//|<link[^>]*rel=""(?:stylesheet|preload|icon)""[^>]*href=""(?:https?:)?//",
                         RegexOptions.IgnoreCase))
                Assert.Fail($"{key} {path}: dış kaynak {m.Value}");
            Assert.All(theme.FontPreloads, f => Assert.Contains(Path.GetFileNameWithoutExtension(f), html));

            // Slotlar gerçekten basıldı (temadan bağımsız işaretler). Görünen metin = betik blokları atılmış HTML.
            var visible = WebUtility.HtmlDecode(ScriptBlock().Replace(html, ""));
            Assert.True(Regex.Matches(html, @"<h1[\s>]").Count == 1, $"{key} {path}: tek h1 değil"); // Hero / DetailHero / sayfa
            if (path is "/" or "/musaitlik" || path == dated)
                Assert.Contains($"href=\"/araclar/{slug}\"", html); // Home/Fleet ilanı basıldı
            if (path == dated)
                Assert.Contains("role=\"status\"", html);            // arama sonucu duyurulur (FleetSummary.Canli)
            if (path == "/")
            {
                // SEO L-4: görünen SSS = FAQPage JSON-LD (ana sayfa özeti 4; 5. ve 6. yalnız /sss'te).
                Assert.Equal(4, Regex.Matches(html, @"""@type"":""Question""").Count);
                foreach (var i in new[] { 1, 2, 3, 4 }) Assert.Contains($"Soru {i}?", visible);
                Assert.DoesNotContain("Soru 5?", visible);
            }
            if (path == $"/araclar/{slug}")
            {
                // DetailHero: tarife satırları sayfadan — haftalık toplam 9.100, gün başı 9.100 / 7 = 1.300 (elle).
                Assert.Contains("₺9.100", visible);
                Assert.Contains("₺1.300", visible);
                Assert.Matches($@"name=""ilan""[^>]*value=""{Regex.Escape(slug)}""|value=""{Regex.Escape(slug)}""[^>]*name=""ilan""", html);
            }
        }
    }

    // Boş filo: Fleet slotu sayfanın verdiği boş durumu basar (tarihsiz ve tarihli), ana sayfada filo bölümü yok (T2).
    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public async Task Her_tema_bos_filo_durumunu_basar(string key)
    {
        var tenant = await SeedTenantAsync(key);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var browse = WebUtility.HtmlDecode(await client.GetStringAsync("/musaitlik"));
        Assert.Contains("Şu anda sitede yayınlanmış araç yok", browse);
        Assert.DoesNotContain("role=\"status\"", browse);

        var search = WebUtility.HtmlDecode(await client.GetStringAsync($"/musaitlik?bas={Day(30)}&bit={Day(33)}"));
        Assert.Contains("Bu tarihlerde müsait araç yok", search);
        Assert.Contains("role=\"status\"", search);
        Assert.Contains("href=\"/iletisim\"", search);

        var home = await client.GetStringAsync("/");
        Assert.Contains($"data-tema=\"{key}\"", home);
        Assert.DoesNotContain("href=\"/araclar/", home);
    }

    [Fact]
    public async Task Onizleme_parametresi_temayi_degistirir_noindex_yazar_gecersiz_ad_yok_sayilir()
    {
        var tenant = await SeedTenantAsync(null); // kayıtlı tema yok → anahtar (varsayılan)
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        static string Css(string key) => $@"css/{Path.GetFileNameWithoutExtension(SiteThemes.Resolve(key).CssPath)}\.[a-z0-9]+\.css";

        using (var r = await client.GetAsync("/?tema=rota"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(Css("rota"), html);
            Assert.Contains("data-tema=\"rota\"", html);
            Assert.Equal("noindex, follow", string.Join(",", r.Headers.GetValues("X-Robots-Tag")));
        }
        using (var r = await client.GetAsync("/?tema=olmayan"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(Css("anahtar"), html);
            Assert.Contains("data-tema=\"anahtar\"", html);
            Assert.False(r.Headers.Contains("X-Robots-Tag"));
        }
        using (var r = await client.GetAsync("/"))
        {
            Assert.Contains("data-tema=\"anahtar\"", await r.Content.ReadAsStringAsync());
            Assert.False(r.Headers.Contains("X-Robots-Tag"));
        }

        // 404 sayfası da önizlenen temayla basılır (durum 404 kalır); diğer sorgu parametreleri taşınmaz.
        using (var r = await client.GetAsync("/araclar/olmayan-ilan?tema=vitrin&x=1"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Matches(Css("vitrin"), html);
            Assert.Contains("data-tema=\"vitrin\"", html);
            Assert.Contains("burada değil", WebUtility.HtmlDecode(html));
        }
        using (var r = await client.GetAsync("/yok/boyle/bir/adres?tema=olmayan"))
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Contains("data-tema=\"anahtar\"", await r.Content.ReadAsStringAsync());
        }
    }

    private static string? RobotsHeader(HttpResponseMessage r)
        => r.Headers.TryGetValues("X-Robots-Tag", out var v) ? string.Join(",", v) : null;

    /// <summary>
    /// <c>?palet=</c> ve <c>?renk=</c> önizlemesi <c>?tema=</c> ile aynı desen: geçerli değer o istekte basılır ve
    /// noindex yazar; geçersiz değer yok sayılır (kayıtlı görünüm, noindex yok); 404 sayfası geçerli önizlemeyi taşır.
    /// Beklenen renkler setlerin elle yazılmış zeminleri (#edf0f2 pazar/domates varsayılan, #eef1f6 pazar/kobalt,
    /// #2a161b vitrin/bordo — koyu kimlik, #efede8 vitrin/sampanya).
    /// </summary>
    [Fact]
    public async Task Palet_ve_renk_onizlemesi_noindex_yazar_gecersiz_deger_yok_sayilir_404_tasir()
    {
        var tenant = await SeedTenantAsync("pazar"); // kayıtlı tema pazar, set yok → varsayılan set (domates)
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        using (var r = await client.GetAsync("/?palet=kobalt"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("<style>:root{--bg:#eef1f6;", html);
            Assert.Contains("<meta name=\"theme-color\" content=\"#eef1f6\" media=\"(prefers-color-scheme: light)\" />", html);
            Assert.Contains("<meta name=\"theme-color\" content=\"#11141c\" media=\"(prefers-color-scheme: dark)\" />", html);
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }
        // Başka temanın seti: önizleme parametresi biçimce geçerli (noindex) ama bu temada yok → temanın varsayılan seti.
        using (var r = await client.GetAsync("/?palet=bordo"))
        {
            Assert.Contains("<style>:root{--bg:#edf0f2;", await r.Content.ReadAsStringAsync());
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }
        using (var r = await client.GetAsync("/?tema=vitrin&palet=bordo"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Matches(@"css/tema-vitrin\.[a-z0-9]+\.css", html);
            Assert.Contains("<style>:root{--bg:#2a161b;", html); // vitrin koyu öncelikli: bordo'nun açık şeması da koyu
            Assert.Contains("<meta name=\"theme-color\" content=\"#2a161b\" media=\"(prefers-color-scheme: light)\" />", html);
        }
        // Özel ana renk: yalnız KESİN #rrggbb (adreste %23); küçük harfle, Rgb'den yeniden yazılarak basılır.
        using (var r = await client.GetAsync("/?renk=%23C0392B"))
        {
            Assert.Contains("--brand:#c0392b;", await r.Content.ReadAsStringAsync());
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }

        // Geçersiz değerler yok sayılır: varsayılan görünüm, noindex YOK, ham değer sayfaya sızmaz.
        foreach (var bad in new[]
                 {
                     "/?palet=olmayan", "/?palet=KOBALT;}", "/?renk=c0392b", "/?renk=%23c0392b;}", "/?renk=red",
                     "/?renk=%23c0392b%3C%2Fstyle%3E", "/?palet=kobalt&palet=hardal",
                 })
        {
            using var r = await client.GetAsync(bad);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("<style>:root{--bg:#edf0f2;", html);
            Assert.Contains("--brand:#d42a1c;", html);
            Assert.DoesNotContain("c0392b", html);
            Assert.True(RobotsHeader(r) is null, $"{bad}: {RobotsHeader(r)}");
        }

        // 404 sayfası geçerli tema + palet + renk'i taşır (diğer parametreler düşer); durum 404 kalır.
        // Vitrin'in açık zeminli seti (Şampanya, #efede8): #c0392b o zeminde ve kartta 3:1'i tutar → aynen basılır.
        using (var r = await client.GetAsync("/yok/boyle/bir/adres?tema=vitrin&palet=sampanya&renk=%23c0392b&x=1"))
        {
            var html = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Matches(@"css/tema-vitrin\.[a-z0-9]+\.css", html);
            Assert.Contains("<style>:root{--bg:#efede8;", html);
            Assert.Contains("--brand:#c0392b;", html);
            Assert.Equal("noindex, follow", RobotsHeader(r));
        }
    }

    /// <summary>Kayıtlı set ve özel ana renk basılır; önizleme kaydı ezer; kayıtlı set temaya ait değilse (tema sonradan
    /// değişti — servis yalnız biçimi doğrular) temanın varsayılan seti basılır.</summary>
    [Fact]
    public async Task Kayitli_set_ve_ozel_renk_basilir_temaya_ait_olmayan_set_varsayilana_duser()
    {
        var tenant = await SeedTenantAsync("rota", "kayisi", "#c0392b");
        using (var factory = new PublicSiteFactory(fx, tenant))
        {
            var client = factory.Client();
            var html = await client.GetStringAsync("/");
            Assert.Matches(@"css/tema-rota\.[a-z0-9]+\.css", html);
            Assert.Contains("<style>:root{--bg:#f3f4f6;", html);
            Assert.Contains("--brand:#c0392b;", html); // #c0392b zeminde 3:1'i tutuyor → aynen
            using var r = await client.GetAsync("/?palet=deniz&renk=%230b1f3a");
            var preview = await r.Content.ReadAsStringAsync();
            Assert.Contains("<style>:root{--bg:#f1f5f6;", preview);
            Assert.Contains("--brand:#0b1f3a;", preview);
        }

        var stale = await SeedTenantAsync("pazar", "kayisi");
        using (var factory = new PublicSiteFactory(fx, stale))
            Assert.Contains("<style>:root{--bg:#edf0f2;", await factory.Client().GetStringAsync("/"));
    }

    // Eski önizleme bağlantıları KALICI çalışır: ?tema=tarife → pazar, ?tema=kontuar → rota; yine önizleme sayılır
    // (noindex), 404 sayfası da eşlenen temayla basılır. Kayıtlı tema vitrin: eşleme gerçekten temayı değiştiriyor.
    [Fact]
    public async Task Eski_anahtarli_onizleme_yeni_temayla_basilir_ve_noindex()
    {
        var tenant = await SeedTenantAsync("vitrin");
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        foreach (var (legacy, current) in new[] { ("tarife", "pazar"), ("KONTUAR", "rota") })
        {
            using (var r = await client.GetAsync($"/?tema={legacy}"))
            {
                var html = await r.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Contains($"data-tema=\"{current}\"", html);
                Assert.Matches($@"css/{Path.GetFileNameWithoutExtension(SiteThemes.Resolve(current).CssPath)}\.[a-z0-9]+\.css", html);
                Assert.Equal("noindex, follow", string.Join(",", r.Headers.GetValues("X-Robots-Tag")));
            }
            using (var r = await client.GetAsync($"/yok/boyle/bir/adres?tema={legacy}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
                Assert.Contains($"data-tema=\"{current}\"", await r.Content.ReadAsStringAsync());
            }
        }
        using (var r = await client.GetAsync("/"))
            Assert.Contains("data-tema=\"vitrin\"", await r.Content.ReadAsStringAsync());
    }

    // Migration'dan önce okunmuş / elle yazılmış eski değer de doğru temayı basar (site ham değeri normalize eder).
    [Fact]
    public async Task Kayitli_eski_anahtar_yeni_temayla_basilir()
    {
        var tenant = await SeedTenantAsync(null);
        await SeedSettingsRowAsync(tenant);           // ayar satırı tembel oluşur
        await WriteRawThemeAsync(tenant, "kontuar");

        using var factory = new PublicSiteFactory(fx, tenant);
        var html = await factory.Client().GetStringAsync("/");
        Assert.Contains("data-tema=\"rota\"", html);
    }

    private async Task SeedSettingsRowAsync(Guid tenant)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(tenant);
        var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
        await svc.SaveAsync(await svc.GetAsync());
    }

    /// <summary>Servisi atlayarak ham değer yazar (servis eski anahtarı normalize ederdi) — firmanın kendi RLS
    /// bağlamında, racar_app ile.</summary>
    private async Task WriteRawThemeAsync(Guid tenant, string value)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using (var up = new NpgsqlCommand(
                         "update \"Ayarlar\" set \"SiteTemasi\" = @v where \"TenantId\" = @t", conn, tx))
        {
            up.Parameters.AddWithValue("v", value);
            up.Parameters.AddWithValue("t", tenant);
            Assert.Equal(1, await up.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }
}
