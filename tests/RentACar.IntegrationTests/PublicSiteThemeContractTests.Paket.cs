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
        // Kurumsal bölüm: GET formu mevcut talep sayfasına (yeni uç yok), not alanı taşınır.
        Assert.Contains("id=\"kurumsal\"", home);
        Assert.Matches(@"<form method=""get"" action=""/rezervasyon-talebi""[^>]*aria-label=""Kurumsal teklif""", home);
        Assert.Contains("<textarea name=\"not\"", home);
        Assert.Single(Regex.Matches(home, @"<h1[\s>]"));

        // Talep formu not alanını ön doldurur; içerik Razor'ca kaçışlanır (etiket enjekte edilemez).
        var request = await client.GetStringAsync("/rezervasyon-talebi?bas=" + Day(10) + "&bit=" + Day(40)
                                                  + "&not=" + Uri.EscapeDataString("5 araç, 12 ay <b>İzmir</b>"));
        Assert.Contains("5 araç, 12 ay <b>İzmir</b></textarea>", WebUtility.HtmlDecode(request)); // görünen metin
        Assert.Contains("&lt;b&gt;", request);                                                    // ham HTML'de kaçışlı
        Assert.DoesNotMatch(@"<b>[^<]*zmir</b>", request);

        // Liste: aylık toplamı olan ilan "/ ay" ile; olmayan günlük fiyatla.
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/musaitlik"));
        Assert.Matches(@"₺32\.000</span> <span class=""pk-birim"">/ ay", list);
        Assert.Matches(@"₺1\.500</span> <span class=""pk-birim"">/ gün", list);
    }
}
