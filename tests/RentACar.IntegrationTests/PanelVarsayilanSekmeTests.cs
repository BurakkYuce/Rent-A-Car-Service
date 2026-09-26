using RentACar.Web.Api.Panel;

namespace RentACar.IntegrationTests;

/// <summary>
/// Panel "Dönüşler" / "Çıkışlar" kartlarının varsayılan sekmesi ve Gecikmiş çipinin acil görünümü.
///
/// <para><b>Canlı kanıt:</b> 9 gecikmiş dönüş (en eskisi 08.07.2026) varken pano "Bugün 0 — Kayıt
/// yok." ile açılıyordu; "Gecikmiş 9" çipi "Yarın 0" ile aynı griydi. En acil iş, kullanıcı bir
/// sekmeye tıklamadıkça görünmüyordu.</para>
///
/// <para>Bağımsız oracle: beklenen değerler aşağıda ELLE yazılmış doğruluk tablosudur
/// ("gec"/"bugun"/"yarin" düz metin — <see cref="PanelTab"/> sabitlerinden türetilmez; sabitin
/// değeri değişirse URL sözleşmesi de değişmiş demektir ve bu test bunu yakalamalı).</para>
/// </summary>
public sealed class PanelVarsayilanSekmeTests
{
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
    public void Etkin_sekme_dogruluk_tablosu(string? raw, int overdue, string expected)
        => Assert.Equal(expected, PanelTab.Active(raw, overdue));

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
    public void Secilen_yalniz_bilinen_uc_degeri_tanir(string? raw, string? expected)
        => Assert.Equal(expected, PanelTab.Selected(raw));

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
    public void Cip_sinifi_dogruluk_tablosu(string cip, string active, int overdue, string expected)
        => Assert.Equal(expected, PanelTab.ChipClass(cip, active, overdue));

    [Fact]
    public void Canli_vaka_parametresiz_9_gecikmis_donus_Gecikmis_sekmesinde_acilir()
    {
        // Pano "/" ile açıldı (df yok), 9 gecikmiş dönüş, bugün 0.
        var active = PanelTab.Active(null, 9);
        Assert.Equal("gec", active);
        Assert.Equal("on acil", PanelTab.ChipClass("gec", active, 9));
        Assert.Equal("", PanelTab.ChipClass("bugun", active, 9));
        Assert.Equal("", PanelTab.ChipClass("yarin", active, 9));

        // Kullanıcı bilerek "Bugün"e geçti → Bugün açılır ama Gecikmiş çipi kırmızı kalır.
        var today = PanelTab.Active("bugun", 9);
        Assert.Equal("bugun", today);
        Assert.Equal("acil", PanelTab.ChipClass("gec", today, 9));
        Assert.Equal("on", PanelTab.ChipClass("bugun", today, 9));
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
    public void Cikislar_etkin_sekme_gecikmis_sayisina_bakmaz(string? raw, string expected)
        => Assert.Equal(expected, PanelTab.IsPickupActive(raw));

    [Fact]
    public void Cikislarda_bayat_no_show_varken_Bugun_acilir_ama_Gecikmis_cipi_acil_kalir()
    {
        // Canlı-benzeri vaka: 1 bayat no-show (gecikmiş çıkış), bugün 3 çıkış, pano "/" ile açıldı.
        var active = PanelTab.IsPickupActive(null);
        Assert.Equal("bugun", active);
        Assert.Equal("on", PanelTab.ChipClass("bugun", active, 1));
        Assert.Equal("acil", PanelTab.ChipClass("gec", active, 1));  // bekleyen iş görünür kalır

        // Aynı sayılarla Dönüşler kuralı Gecikmiş'i açardı — iki kartın farkı bilinçli.
        Assert.Equal("gec", PanelTab.Active(null, 1));
    }

    // F13.1a: Blazor Panel (Home.razor) silindi; "kararı ham df/cf'den verme" kaynak çiti anlamını yitirdi. Kural
    // yukarıdaki saf PanelTab testlerinde ve sunucunun hesapladığı `varsayilanSekme`'de (PanelApi → PanelTab) yaşar.

    // F13.1b: Blazor kabuğunun "acil çip metin rengi" (ColorContrast + app.css + MainLayout) testleri kabukla birlikte
    // silindi; yeni arayüz rozet renklerini kendi tasarım token'larından alır (stil denetimi `npm run lint`).
}
