using System.Net;
using System.Text.RegularExpressions;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Paket teması: aylık fiyat yalnız ilanda girilmişse (uydurulmaz), aylık fiyat tablosu, kurumsal teklif bölümü
/// mevcut talep formuna GET ile geçer (yeni POST ucu yok) ve not alanı ön doldurulur (kaçışlanmış). BAĞIMSIZ ORACLE:
/// tutarlar elle kurulan ilanlardan yazıldı.
/// </summary>
public sealed partial class PublicSiteThemeContractTests
{
    [Fact]
    public async Task Paket_aylik_fiyat_yalniz_ilanda_girilmisse_ve_kurumsal_teklif_talep_formuna_gecer()
    {
        var tenant = await SeedTenantAsync("paket");
        var weeklyOnly = await SeedContentAsync(tenant);                       // Egea: günlük 1.500, aylık YOK
        var monthly = await SeedAutomaticListingAsync(tenant, monthly: 32000m); // Clio: günlük 1.800, 30 gün 32.000

        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("data-tema=\"paket\"", home);
        // Hero: en düşük AYLIK fiyat = 32.000 (yalnız Clio'da girilmiş) — Egea için 1.500 × 30 uydurulmaz.
        Assert.Contains("En düşük aylık fiyat", home);
        Assert.Contains("₺32.000", home);
        Assert.DoesNotContain("₺45.000", home);
        // Aylık tablo: Clio satırında 32.000, Egea satırında teklif bağlantısı.
        Assert.Contains("Aylık fiyatlar", home);
        Assert.Matches($@"href=""/araclar/{Regex.Escape(weeklyOnly)}"">[^<]*</a></th>\s*<td>\s*<a href=""/rezervasyon-talebi\?ilan={Regex.Escape(weeklyOnly)}", home);
        // Kurumsal bölüm: GET formu mevcut talep sayfasına (yeni uç yok). KVKK: GET formunda serbest metin alanı YOK —
        // adrese yalnız tarihler ve kurumsal işareti girer.
        Assert.Contains("id=\"kurumsal\"", home);
        var corporate = Regex.Match(home, @"<form method=""get"" action=""/rezervasyon-talebi""[^>]*aria-label=""Kurumsal teklif"">(?<body>.*?)</form>",
            RegexOptions.Singleline);
        Assert.True(corporate.Success, "kurumsal GET formu yok");
        var fields = Regex.Matches(corporate.Groups["body"].Value, @"name=""(?<n>[^""]+)""").Select(m => m.Groups["n"].Value);
        Assert.Equal(["bas", "bit", "kurumsal"], fields);
        Assert.DoesNotContain("<textarea", corporate.Groups["body"].Value);
        Assert.Single(Regex.Matches(home, @"<h1[\s>]"));

        // Talep sayfası: kurumsal=1 başlığı değiştirir; adresle gelen serbest metin (not) ön doldurulmaz.
        var request = WebUtility.HtmlDecode(await client.GetStringAsync(
            $"/rezervasyon-talebi?bas={Day(10)}&bit={Day(40)}&kurumsal=1&not=" + Uri.EscapeDataString("5 araç İzmir")));
        Assert.Contains("<h1>Kurumsal teklif</h1>", request);
        Assert.Contains("maxlength=\"2000\"", request);
        Assert.DoesNotContain("5 araç İzmir", request);

        // Liste: aylık toplamı olan ilan "/ ay" ile; olmayan günlük fiyatla.
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/musaitlik"));
        Assert.Matches(@"₺32\.000</span> <span class=""pk-birim"">/ ay", list);
        Assert.Matches(@"₺1\.500</span> <span class=""pk-birim"">/ gün", list);
    }
}
