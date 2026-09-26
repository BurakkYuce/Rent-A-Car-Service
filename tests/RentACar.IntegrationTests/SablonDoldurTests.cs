using RentACar.Application.Notifications;

namespace RentACar.IntegrationTests;

/// <summary>
/// Şablon yer tutucu doldurma — SAF mantık, BAĞIMSIZ ORACLE: beklenen çıktılar elle yazılmıştır,
/// <see cref="TemplateFiller"/>'un kendi mantığından türetilmemiştir.
/// </summary>
public sealed class SablonDoldurTests
{
    private static Dictionary<string, string?> D(params (string, string?)[] c)
        => c.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void Bilinen_yer_tutucular_dolar()
    {
        var output = TemplateFiller.Fill(
            "Sayın {MusteriAd}, {Plaka} plakalı aracınız {CikisTarih} tarihinde hazır.",
            D(("MusteriAd", "Ahmet Yılmaz"), ("Plaka", "34ABC123"), ("CikisTarih", "18.08.2026 10:00")),
            htmlEscape: false);

        Assert.Equal("Sayın Ahmet Yılmaz, 34ABC123 plakalı aracınız 18.08.2026 10:00 tarihinde hazır.", output);
    }

    [Fact]
    public void Bilinmeyen_yer_tutucu_oldugu_gibi_kalir()
    {
        // Sessizce boşa çevirmek "Sayın , aracınız hazır" üretirdi — yazım hatası görünmez olurdu.
        var output = TemplateFiller.Fill("Sayın {Musteri_Ad}, hoş geldiniz.", D(("MusteriAd", "Ayşe")), htmlEscape: false);
        Assert.Equal("Sayın {Musteri_Ad}, hoş geldiniz.", output);
    }

    [Fact]
    public void Bilinen_anahtarin_null_degeri_bos_stringe_cevrilir()
    {
        var output = TemplateFiller.Fill("Plaka: {Plaka}.", D(("Plaka", null)), htmlEscape: false);
        Assert.Equal("Plaka: .", output);
    }

    [Fact]
    public void Eposta_govdesinde_deger_html_kacisli_sablonun_html_i_korunur()
    {
        // Şablonun kendi <b> etiketi FİRMANIN yazdığıdır → korunur.
        // Müşteri adındaki <script> ise VERİDİR → kaçırılır, e-postada çalışmaz.
        var output = TemplateFiller.Fill(
            "<p>Sayın <b>{MusteriAd}</b>,</p>",
            D(("MusteriAd", "<script>alert(1)</script>")),
            htmlEscape: true);

        Assert.Contains("<p>", output);
        Assert.Contains("<b>", output);
        Assert.DoesNotContain("<script>", output);
        Assert.Contains("&lt;script&gt;", output);
    }

    [Fact]
    public void Sms_govdesinde_kacis_uygulanmaz()
    {
        // Düz metinde kaçış "Yüce &amp; Ortakları" gibi bozuk çıktı üretirdi.
        var output = TemplateFiller.Fill("Sayın {MusteriAd}", D(("MusteriAd", "Yüce & Ortakları")), htmlEscape: false);
        Assert.Equal("Sayın Yüce & Ortakları", output);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Kapanmamış {Plaka", "Kapanmamış {Plaka")]
    [InlineData("Süslü yok", "Süslü yok")]
    public void Sinir_durumlari(string? template, string expected)
        => Assert.Equal(expected, TemplateFiller.Fill(template, D(("Plaka", "34ABC")), htmlEscape: false));

    [Fact]
    public void Duz_metin_html_den_okunabilir_alternatif_uretir()
    {
        var duz = TemplateFiller.PlainText(
            "<p>Sayın Ahmet,</p><p>Aracınız <b>hazır</b>.<br/>İyi yolculuklar.</p>");

        Assert.DoesNotContain("<", duz);
        Assert.Contains("Sayın Ahmet,", duz);
        Assert.Contains("İyi yolculuklar.", duz);
        // Satır sonu üreten etiketler yeni satıra dönmüş olmalı (tek satıra yapışmasın).
        Assert.Contains('\n', duz);
    }

    [Fact]
    public void Duz_metin_html_varliklarini_cozer()
        => Assert.Equal("Yüce & Ortakları", TemplateFiller.PlainText("<p>Yüce &amp; Ortakları</p>"));
}
