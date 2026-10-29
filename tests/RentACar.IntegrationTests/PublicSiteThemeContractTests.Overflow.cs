using System.Text;
using System.Text.RegularExpressions;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

/// <summary>
/// Yatay taşma — YAPISAL kilitler (css kaynaktan okunur). Telefonda yana kayan sayfanın canlıda ölçülen dört kök
/// nedeni kural olarak burada: (1) çıplak <c>fr</c> izi — alt sınırı içeriğin min-content genişliği (tarih alanı
/// ~170 px, iki sütunda 320 px'i aşar); (2) alan şablonlu ya da tek sütunlu ızgaranın örtük <c>auto</c> sütunu — en
/// geniş çocuğa göre büyür; (3) bölünemeyen uzun bağlantı metni (e-posta adresi, sökülen Vitrin 390 px'te 157 px);
/// (4) açılışta dönen öğe (sökülen Pazar'ın fiyat etiketi salınımı ~2 sn 5–8 px). Ölçümün kendisi tarayıcı ister:
/// <c>scripts/site-tasma.mjs</c> (animasyon sırasında + sonrasında, 320/390 px; CI'da değil — PublicSite + veri ister).
/// </summary>
public sealed partial class PublicSiteThemeContractTests
{
    /// <summary>En içteki kural blokları: <c>seçici { bildirimler }</c> (medya sorgusu içindekiler dahil).</summary>
    [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
    private static partial Regex RuleBlock();

    [GeneratedRegex(@"grid-template-columns\s*:\s*([^;}]+)")]
    private static partial Regex GridColumns();

    /// <summary><c>minmax(…)</c> dışında kalan <c>fr</c> izi (1fr, 2fr, 1.5fr).</summary>
    [GeneratedRegex(@"(?<![\w.-])\d*\.?\d+fr\b")]
    private static partial Regex BareFr();

    [GeneratedRegex(@"@keyframes\s+[\w-]+\s*\{")]
    private static partial Regex KeyframesStart();

    [GeneratedRegex(@"(?<![\w-])rotate\s*:|rotate[XYZ3d]*\(")]
    private static partial Regex Rotation();

    [GeneratedRegex(@"white-space\s*:\s*(nowrap|pre)\b|overflow-wrap\s*:\s*normal|word-break\s*:\s*keep-all")]
    private static partial Regex UnbreakableText();

    [GeneratedRegex(@"overflow-x\s*:\s*clip\b")]
    private static partial Regex OverflowXClip();

    private static IEnumerable<(string File, string Css)> AllSiteCss() =>
        new[] { "base.css" }.Concat(SiteThemes.All.Select(ThemeFile)).Select(f => (f, ReadCss(f)));

    private static IEnumerable<(string Selector, string Body)> Rules(string css) =>
        RuleBlock().Matches(css).Select(m => (m.Groups[1].Value.Trim(), m.Groups[2].Value));

    /// <summary><c>minmax(...)</c> gruplarını (iç içe parantez dahil) atar; geriye kalan çıplak izlerdir.</summary>
    private static string WithoutMinmax(string value)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length;)
        {
            if (string.CompareOrdinal(value, i, "minmax(", 0, 7) == 0)
            {
                var depth = 0;
                for (; i < value.Length; i++)
                {
                    if (value[i] == '(') depth++;
                    else if (value[i] == ')' && --depth == 0) { i++; break; }
                }
                continue;
            }
            sb.Append(value[i++]);
        }
        return sb.ToString();
    }

    private static IEnumerable<string> KeyframeBodies(string css)
    {
        foreach (Match m in KeyframesStart().Matches(css))
        {
            var start = m.Index + m.Length;
            var depth = 1;
            var i = start;
            for (; i < css.Length && depth > 0; i++)
            {
                if (css[i] == '{') depth++;
                else if (css[i] == '}') depth--;
            }
            yield return css[start..(i - 1)];
        }
    }

    [Theory]
    [InlineData("1fr 1fr", true)]
    [InlineData("repeat(2, 1fr)", true)]
    [InlineData("1.5fr auto", true)]
    [InlineData("repeat(2, minmax(0, 1fr))", false)]
    [InlineData("repeat(auto-fill, minmax(min(100%, 290px), 1fr))", false)]
    [InlineData("minmax(0, 1.55fr) minmax(320px, 1fr)", false)]
    [InlineData("2.25rem minmax(0, 1fr)", false)]
    public void Ciplak_fr_denetimi_kendini_dogrular(string value, bool bare)
        => Assert.Equal(bare, BareFr().IsMatch(WithoutMinmax(value)));

    /// <summary>
    /// Izgara izi <c>minmax(0, Nfr)</c> ile yazılır: çıplak <c>1fr</c>'ın alt sınırı içeriğin min-content genişliğidir
    /// ve dar ekranda sütunu (dolayısıyla sayfayı) içeriğe göre genişletir. <c>minmax(&lt;uzunluk&gt;, 1fr)</c> serbest
    /// (alt sınır yazanın bilinçli seçimi).
    /// </summary>
    [Fact]
    public void Izgara_izleri_icerik_genisligine_kilitlenmez()
    {
        var bad = AllSiteCss()
            .SelectMany(f => GridColumns().Matches(f.Css).Select(m => (f.File, Value: m.Groups[1].Value.Trim())))
            .Where(x => BareFr().IsMatch(WithoutMinmax(x.Value)))
            .Select(x => $"{x.File}: grid-template-columns: {x.Value}")
            .ToList();
        Assert.True(bad.Count == 0, "Çıplak fr izi — minmax(0, Nfr) yazılmalı:\n" + string.Join("\n", bad));
    }

    /// <summary>
    /// Alan şablonlu ızgara sütununu aynı kuralda AÇIKÇA tanımlar: şablonun örtük sütunu <c>auto</c>'dur ve en geniş
    /// çocuğa (arama formu, kapak) göre büyür — ilan detayı 320 px'te 14 px taşıyordu.
    /// </summary>
    [Fact]
    public void Alan_sablonlu_izgara_sutununu_acikca_tanimlar()
    {
        var bad = AllSiteCss()
            .SelectMany(f => Rules(f.Css).Select(r => (f.File, r.Selector, r.Body)))
            .Where(r => r.Body.Contains("grid-template-areas", StringComparison.Ordinal)
                && !r.Body.Contains("grid-template-columns", StringComparison.Ordinal))
            .Select(r => $"{r.File}: {r.Selector}")
            .ToList();
        Assert.True(bad.Count == 0, "grid-template-areas var, grid-template-columns yok:\n" + string.Join("\n", bad));
    }

    /// <summary>
    /// Telefonda tek sütunlu kalan ızgaralar (etiket + alan, ilan detayı) sütunu <c>minmax(0, 1fr)</c> ile kurar.
    /// Liste elle (oracle): canlıda taşırdığı ölçülenler.
    /// </summary>
    [Theory]
    [InlineData("base.css", ".alan")]
    [InlineData("base.css", ".detay")]
    public void Tek_sutunlu_izgara_icerige_gore_genislemez(string file, string selector)
    {
        var rule = Rules(ReadCss(file)).FirstOrDefault(r => r.Selector == selector);
        Assert.False(rule == default, $"{file}: {selector} kuralı yok");
        Assert.Matches(@"grid-template-columns\s*:\s*minmax\(0,\s*1fr\)\s*(;|$)", rule.Body);
    }

    /// <summary>
    /// Büyük iletişim bağlantısı (telefon, e-posta) tek uzun sözcüktür: her temada satır içinden bölünebilir
    /// (base.css), hiçbir tema bunu geri almaz.
    /// </summary>
    [Fact]
    public void Buyuk_iletisim_baglantisi_bolunebilir()
    {
        var baseRule = Rules(ReadCss("base.css")).FirstOrDefault(r => r.Selector == ".buyuk-baglanti");
        Assert.False(baseRule == default, "base.css: .buyuk-baglanti kuralı yok");
        Assert.Matches(@"overflow-wrap\s*:\s*anywhere", baseRule.Body);

        var bad = AllSiteCss()
            .SelectMany(f => Rules(f.Css).Select(r => (f.File, r.Selector, r.Body)))
            .Where(r => r.Selector.Contains("buyuk-baglanti", StringComparison.Ordinal) && UnbreakableText().IsMatch(r.Body))
            .Select(r => $"{r.File}: {r.Selector}")
            .ToList();
        Assert.True(bad.Count == 0, "Bölünmeyi geri alan kural:\n" + string.Join("\n", bad));
    }

    /// <summary>
    /// Sayfa yolu (breadcrumb) TEK SATIR: telefonda uzun ilan adı yolu kırıp adı ayrı satıra itiyordu. Liste sarmaz,
    /// son öğe (bulunulan sayfa; adı h1'de) sığmazsa üç noktayla kısalır. Hiçbir tema sarmayı geri açmaz.
    /// </summary>
    [Fact]
    public void Sayfa_yolu_tek_satir_son_oge_kisalir()
    {
        var rules = Rules(ReadCss("base.css")).ToList();
        Assert.Matches(@"flex-wrap\s*:\s*nowrap", rules.Single(r => r.Selector == ".yol ol").Body);
        var current = rules.Single(r => r.Selector == ".yol [aria-current]").Body;
        Assert.Matches(@"text-overflow\s*:\s*ellipsis", current);
        Assert.Matches(@"overflow\s*:\s*hidden", current);
        var bad = AllSiteCss()
            .SelectMany(f => Rules(f.Css).Select(r => (f.File, r.Selector, r.Body)))
            .Where(r => r.Selector.Contains(".yol", StringComparison.Ordinal) && Regex.IsMatch(r.Body, @"flex-wrap\s*:\s*wrap"))
            .Select(r => $"{r.File}: {r.Selector}")
            .ToList();
        Assert.True(bad.Count == 0, "Sayfa yolunu saran kural:\n" + string.Join("\n", bad));
    }

    /// <summary>
    /// Açılışta DÖNEN öğe (anahtar karede <c>rotate</c>) dönüş sırasında kutusunun dışına çıkar; telefonda ekran
    /// kenarına yakın öğe sayfayı yana kaydırır. Böyle bir temada <c>main</c> yatay ekseni kırpar
    /// (<c>overflow-x: clip</c> — kaydırma kabı kurmaz, yapışkan öğeleri ve dikey taşmayı bozmaz). Görünen bir şey
    /// kesilmediğinin ölçüsü <c>scripts/site-tasma.mjs</c>'te (dinlenme hâlinde görünüm alanını aşan öğe yok).
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Donen_acilis_animasyonu_yatay_tasma_uretmez(string key)
    {
        var css = ReadCss(ThemeFile(SiteThemes.Resolve(key)));
        var rotates = KeyframeBodies(css).Any(b => Rotation().IsMatch(b));
        if (!rotates) return;
        Assert.Contains(Rules(css), r => r.Selector == "main" && OverflowXClip().IsMatch(r.Body));
    }
}
