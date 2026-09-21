using System.Text.RegularExpressions;
using RentACar.Web.Components.Pages;

namespace RentACar.IntegrationTests;

/// <summary>
/// Panel "Dönüşler" / "Çıkışlar" kartlarının varsayılan sekmesi ve Gecikmiş çipinin acil görünümü.
///
/// <para><b>Canlı kanıt:</b> 9 gecikmiş dönüş (en eskisi 08.07.2026) varken pano "Bugün 0 — Kayıt
/// yok." ile açılıyordu; "Gecikmiş 9" çipi "Yarın 0" ile aynı griydi. En acil iş, kullanıcı bir
/// sekmeye tıklamadıkça görünmüyordu.</para>
///
/// <para>Bağımsız oracle: beklenen değerler aşağıda ELLE yazılmış doğruluk tablosudur
/// ("gec"/"bugun"/"yarin" düz metin — <see cref="PanelSekme"/> sabitlerinden türetilmez; sabitin
/// değeri değişirse URL sözleşmesi de değişmiş demektir ve bu test bunu yakalamalı).</para>
/// </summary>
public sealed class PanelVarsayilanSekmeTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // ── Etkin sekme: (ham URL değeri, gecikmiş sayısı) → açılan sekme ──────────────────────────
    [Theory]
    // Seçim YOK (parametre yok) → gecikmiş varsa gec, yoksa bugun. Canlı vaka: null + 9 → gec.
    [InlineData(null, 0, "bugun")]
    [InlineData(null, 1, "gec")]   // sınır: tek gecikmiş kayıt yeter
    [InlineData(null, 9, "gec")]
    // Boş değer (?cf= — çip linkleri diğer kartın seçimsiz hâlini böyle taşır) = seçim yok.
    [InlineData("", 0, "bugun")]
    [InlineData("", 9, "gec")]
    [InlineData("   ", 3, "gec")]
    // Tanınmayan değer = seçim yok (vurgu ile liste aynı kararı versin).
    [InlineData("xyz", 0, "bugun")]
    [InlineData("xyz", 9, "gec")]
    // AÇIK seçim DAİMA kazanır — gecikmiş sayısı ne olursa olsun.
    [InlineData("bugun", 9, "bugun")]
    [InlineData("bugun", 0, "bugun")]
    [InlineData("yarin", 9, "yarin")]
    [InlineData("yarin", 0, "yarin")]
    [InlineData("gec", 0, "gec")]  // gecikmiş kalmadıysa bile kullanıcı seçtiyse "Kayıt yok." görür
    [InlineData("gec", 9, "gec")]
    // Büyük/küçük harf farkı seçimi düşürmez; kanonik küçük harfe iner.
    [InlineData("GEC", 0, "gec")]
    [InlineData("Yarin", 9, "yarin")]
    public void Etkin_sekme_dogruluk_tablosu(string? ham, int gecikmis, string beklenen)
        => Assert.Equal(beklenen, PanelSekme.Etkin(ham, gecikmis));

    // ── Seçilen: linklere yazılan normalize ham seçim ──────────────────────────────────────────
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("xyz", null)]
    [InlineData(" gec", null)]     // kırpılmaz: elle bozulmuş değer "seçim yok" sayılır
    [InlineData("gec", "gec")]
    [InlineData("bugun", "bugun")]
    [InlineData("yarin", "yarin")]
    [InlineData("BUGUN", "bugun")]
    public void Secilen_yalniz_bilinen_uc_degeri_tanir(string? ham, string? beklenen)
        => Assert.Equal(beklenen, PanelSekme.Secilen(ham));

    // ── Çip sınıfı: (çip, etkin sekme, gecikmiş sayısı) → CSS sınıfı ───────────────────────────
    [Theory]
    // Gecikmiş çipi: kayıt varsa HANGİ sekme açık olursa olsun "acil".
    [InlineData("gec", "gec", 9, "on acil")]
    [InlineData("gec", "bugun", 9, "acil")]
    [InlineData("gec", "yarin", 9, "acil")]
    [InlineData("gec", "gec", 0, "on")]
    [InlineData("gec", "bugun", 0, "")]
    // Bugün/Yarın çipleri ASLA acil değildir (gecikmiş sayısı onları boyamaz).
    [InlineData("bugun", "bugun", 9, "on")]
    [InlineData("bugun", "gec", 9, "")]
    [InlineData("bugun", "bugun", 0, "on")]
    [InlineData("yarin", "yarin", 9, "on")]
    [InlineData("yarin", "gec", 9, "")]
    [InlineData("yarin", "bugun", 0, "")]
    public void Cip_sinifi_dogruluk_tablosu(string cip, string etkin, int gecikmis, string beklenen)
        => Assert.Equal(beklenen, PanelSekme.CipSinifi(cip, etkin, gecikmis));

    [Fact]
    public void Canli_vaka_parametresiz_9_gecikmis_donus_Gecikmis_sekmesinde_acilir()
    {
        // Pano "/" ile açıldı (df yok), 9 gecikmiş dönüş, bugün 0.
        var etkin = PanelSekme.Etkin(null, 9);
        Assert.Equal("gec", etkin);
        Assert.Equal("on acil", PanelSekme.CipSinifi("gec", etkin, 9));
        Assert.Equal("", PanelSekme.CipSinifi("bugun", etkin, 9));
        Assert.Equal("", PanelSekme.CipSinifi("yarin", etkin, 9));

        // Kullanıcı bilerek "Bugün"e geçti → Bugün açılır ama Gecikmiş çipi kırmızı kalır.
        var bugun = PanelSekme.Etkin("bugun", 9);
        Assert.Equal("bugun", bugun);
        Assert.Equal("acil", PanelSekme.CipSinifi("gec", bugun, 9));
        Assert.Equal("on", PanelSekme.CipSinifi("bugun", bugun, 9));
    }

    // ── Çıkışlar: varsayılan DAİMA Bugün (adversarial bulgu) ───────────────────────────────────
    // Gecikmiş ÇIKIŞ = hiç gelmemiş, kapatan job'ı olmayan açık rezervasyon (no-show). Yerel DB'de
    // 2,5 aylık bir kayıt vardı: Dönüşler'in kuralı burada da işleseydi kart HER açılışta o bayat
    // kayıtla açılır, günün asıl işi olan Bugün'ün çıkışları gizlenirdi. Çip yine acil kalır.
    [Theory]
    [InlineData(null, "bugun")]
    [InlineData("", "bugun")]
    [InlineData("xyz", "bugun")]
    [InlineData("bugun", "bugun")]
    [InlineData("yarin", "yarin")]
    [InlineData("gec", "gec")]     // kullanıcı açıkça seçerse Gecikmiş açılır
    [InlineData("GEC", "gec")]
    public void Cikislar_etkin_sekme_gecikmis_sayisina_bakmaz(string? ham, string beklenen)
        => Assert.Equal(beklenen, PanelSekme.CikisEtkin(ham));

    [Fact]
    public void Cikislarda_bayat_no_show_varken_Bugun_acilir_ama_Gecikmis_cipi_acil_kalir()
    {
        // Canlı-benzeri vaka: 1 bayat no-show (gecikmiş çıkış), bugün 3 çıkış, pano "/" ile açıldı.
        var etkin = PanelSekme.CikisEtkin(null);
        Assert.Equal("bugun", etkin);
        Assert.Equal("on", PanelSekme.CipSinifi("bugun", etkin, 1));
        Assert.Equal("acil", PanelSekme.CipSinifi("gec", etkin, 1));  // bekleyen iş görünür kalır

        // Aynı sayılarla Dönüşler kuralı Gecikmiş'i açardı — iki kartın farkı bilinçli.
        Assert.Equal("gec", PanelSekme.Etkin(null, 1));
    }

    // ── Kaynak çitleri: kararın sayfada tekrar ham değerden verilmesini önler ──────────────────
    [Fact]
    public void Home_ham_df_cf_ile_karar_vermez()
    {
        var home = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/Components/Pages/Home.razor"));

        // Eski hata üç ayrı yerde ham null'ı yorumlamaktı: `Df == "gec"`, `Df is null or "bugun"`,
        // `Df switch {...}`. Herhangi biri geri gelirse vurgu/liste/dönüş adresi yeniden ayrışabilir.
        var ham = Regex.Matches(home, @"\b(Df|Cf)\s*(==|!=|is\b|switch\b)")
            .Select(m => m.Value).ToList();
        Assert.True(ham.Count == 0,
            "Home.razor sekme kararını ham df/cf'den veriyor; PanelSekme.Etkin kullanılmalı:\n  "
            + string.Join("\n  ", ham));

        // Tahsil Et dönüş adresi HAM (normalize) seçimi taşımalı, ETKİNİ DEĞİL (adversarial bulgu):
        // varsayılanla "gec"te açılmış pano "/?df=gec" açık seçimine dönerse gecikmişler kapandığında
        // 120 sn tazeleme kartı "Gecikmiş 0 — Kayıt yok."ta tutar, Bugün'ün dönüşleri gizli kalırdı.
        // Seçimsiz dönüş aynı sekmeyi açar: tahsilat kiranın durumunu (gecikmiş sayısını) değiştirmez.
        Assert.Contains("name=\"donus\" value=\"/?df=@DfSecilen&cf=@CfSecilen\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"/?df=@DfEtkin", home, StringComparison.Ordinal);

        // Çıkışlar kartı Dönüşler'in "gecikmiş varsa gec" kuralını KULLANMAZ (bayat no-show).
        Assert.Contains("PanelSekme.CikisEtkin(Cf)", home, StringComparison.Ordinal);
        Assert.DoesNotContain("PanelSekme.Etkin(Cf", home, StringComparison.Ordinal);

        // 6 çipin (2 kart × 3 sekme) tümü sınıfını aynı fonksiyondan alır.
        Assert.Equal(6, Regex.Matches(home, @"class=""@PanelSekme\.CipSinifi\(").Count);
    }

    // ── Acil çipin metin rengi zeminden türetilir (adversarial bulgu) ──────────────────────────
    // Zemin tenant'ın serbest seçtiği "Gecikenler" rengi; sabit beyaz metin sarı seçen tenant'ta
    // ~1,3:1 kontrastla okunmuyordu. Beklenenler ELLE: WCAG oranları bilinen renk çiftlerinden.
    [Theory]
    [InlineData("#fde047", "#0f172a")]   // yellow-300 → koyu metin (beyazla ~1,3:1)
    [InlineData("#facc15", "#0f172a")]   // yellow-400
    [InlineData("#ffffff", "#0f172a")]
    [InlineData("#808080", "#0f172a")]   // orta gri: koyuyla ~5:1, beyazla ~3,9:1
    [InlineData("#dc2626", "#ffffff")]   // varsayılan kırmızı → beyaz (bugünkü görünüm korunur)
    [InlineData("#1e3a8a", "#ffffff")]   // koyu lacivert
    [InlineData("#000000", "#ffffff")]
    [InlineData("#DC2626", "#ffffff")]   // büyük harf hex
    public void Acil_cip_metin_rengi_zeminden_turetilir(string zemin, string beklenen)
        => Assert.Equal(beklenen, RentACar.Web.Components.Layout.RenkKontrast.UzerindekiMetin(zemin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fde047")]
    [InlineData("#fde04")]
    [InlineData("#gggggg")]
    [InlineData("# de047")]
    [InlineData("#-de047")]
    public void Bicimsiz_renk_icin_metin_rengi_uretilmez(string? zemin)
        => Assert.Null(RentACar.Web.Components.Layout.RenkKontrast.UzerindekiMetin(zemin));

    [Fact]
    public void Acil_cip_metin_rengini_sabit_beyazdan_degil_degiskenden_alir()
    {
        var css = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/wwwroot/app.css"));
        var kurallar = Regex.Matches(css, @"(?<s>[^{}]*\.dc-tabs a\.acil[^{}]*)\{(?<g>[^}]*)\}");
        Assert.NotEmpty(kurallar);
        foreach (Match k in kurallar)
            if (Regex.IsMatch(k.Groups["g"].Value, @"(^|;)\s*color\s*:"))
                Assert.Contains("var(--tr-renk-gecikenler-on", k.Groups["g"].Value, StringComparison.Ordinal);

        var layout = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/Components/Layout/MainLayout.razor"));
        Assert.Contains("RenkKontrast.UzerindekiMetin(renk)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Acil_cip_stili_app_css_te_tanimli()
    {
        // `acil` sınıfı stilsiz kalırsa Gecikmiş çipi yine gri görünür ve hata SESSİZCE geri gelir.
        var css = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/wwwroot/app.css"));
        Assert.Contains(".dc-tabs a.acil", css, StringComparison.Ordinal);
    }
}
