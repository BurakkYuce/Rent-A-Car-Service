using RentACar.Application.SiteIcerik;
using Microsoft.Extensions.DependencyInjection;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class PublicSiteSeoTests
{
    /// <summary>
    /// H-1 + M-9: "arama dışı" yazı sitemap'e ve llms.txt'e GİRMEZ; ilan, sayfa ve yazı gerçek
    /// son-değişiklik zamanını lastmod olarak taşır. Senaryo: yazı 20 gün önce yayınlandı, 10 gün
    /// önce düzenlendi → lastmod = 10 gün önce. İlan 15 gün önce, özelliği 5 gün önce değişti →
    /// lastmod = 5 gün önce (özellik tablosu sayfada basılıyor). Sayfa 3 gün önce düzenlendi.
    /// </summary>
    [Fact]
    public async Task Sitemap_ve_llms_arama_disi_yaziyi_listelemez_ve_gercek_lastmod_basar()
    {
        var (t, hostName) = await SeedTenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        var root = $"https://{hostName}";

        var visible = await SeedPostAsync(host, t, "Kış lastiği rehberi", outOfSearch: false);
        await SeedPostAsync(host, t, "Personel duyurusu", outOfSearch: true);
        var published = TestZaman.Now().AddDays(-20);
        var edited = TestZaman.Now().AddDays(-10);
        await SetTimeAsync(t, "BlogYazilari", visible, "YayinTarihi", published);
        await SetTimeAsync(t, "BlogYazilari", visible, "UpdatedAtUtc", edited);

        var (listingId, listingSlug) = await SeedListingAsync(host, t);
        var listingEdited = TestZaman.Now().AddDays(-15);
        var featureEdited = TestZaman.Now().AddDays(-5);
        await SetTimeAsync(t, "WebIlanlar", listingId, "UpdatedAtUtc", listingEdited);
        Guid featureId;
        using (var s = host.ScopeFor(t))
            featureId = (await s.ServiceProvider.GetRequiredService<RentACar.Application.WebSite.WebListingService>()
                .GetAsync(listingId))!.Ozellikler.Single().Id;
        await SetTimeAsync(t, "WebIlanOzellikler", featureId, "UpdatedAtUtc", featureEdited);

        Guid pageId;
        using (var s = host.ScopeFor(t))
            pageId = await s.ServiceProvider.GetRequiredService<SiteContentService>()
                .SaveAsync(new SayfaIcerikInput(null, "Hakkımızda", "Biz kimiz.", null, null, 0, true));
        var pageEdited = TestZaman.Now().AddDays(-3);
        await SetTimeAsync(t, "SayfaIcerikler", pageId, "UpdatedAtUtc", pageEdited);

        using var factory = new PublicSiteFactory(fx, t);
        var client = factory.Client();

        var sitemap = ParseSitemap(await client.GetStringAsync("/sitemap.xml"));
        Assert.Equal(W3c(edited), sitemap[$"{root}/blog/kis-lastigi-rehberi"]);
        Assert.False(sitemap.ContainsKey($"{root}/blog/personel-duyurusu"));
        Assert.Equal(W3c(edited), sitemap[$"{root}/blog"]); // tek aranabilir yazının zamanı
        Assert.Equal(W3c(featureEdited), sitemap[$"{root}/araclar/{listingSlug}"]);
        Assert.Equal(W3c(pageEdited), sitemap[$"{root}/hakkimizda"]);

        var llms = await client.GetStringAsync("/llms.txt");
        Assert.Contains($"{root}/blog/kis-lastigi-rehberi", llms);
        Assert.DoesNotContain("personel-duyurusu", llms);
        Assert.Contains("## Blog / Rehber (1 yazı)", llms);
    }

    /// <summary>Yayındaki TÜM yazılar arama dışıysa `/blog` sitemap'te ve llms.txt'te yer almaz.</summary>
    [Fact]
    public async Task Tum_yazilar_arama_disiysa_blog_girisi_yok()
    {
        var (t, hostName) = await SeedTenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        await SeedPostAsync(host, t, "Kampanya duyurusu", outOfSearch: true);

        using var factory = new PublicSiteFactory(fx, t);
        var client = factory.Client();

        var sitemap = ParseSitemap(await client.GetStringAsync("/sitemap.xml"));
        Assert.False(sitemap.ContainsKey($"https://{hostName}/blog"));
        Assert.DoesNotContain(sitemap.Keys, k => k.Contains("/blog/", StringComparison.Ordinal));
        Assert.DoesNotContain("/blog", await client.GetStringAsync("/llms.txt"));
    }

    /// <summary>M-3: robots.txt — işlem sayfaları ve parametreli müsaitlik kapalı; AI arama
    /// tarayıcıları kendi gruplarında AÇIK ama aynı Disallow'larla. Metin elle yazıldı.</summary>
    [Fact]
    public async Task Robots_islem_sayfalarini_ve_parametreli_aramayi_kapatir_AI_taraycilari_acik()
    {
        var (t, hostName) = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, t);

        var body = (await factory.Client().GetStringAsync("/robots.txt")).Replace("\r\n", "\n");

        const string rules = "Allow: /\nDisallow: /rezervasyon-talebi\nDisallow: /talep-alindi\n"
            + "Disallow: /cok-istek\nDisallow: /musaitlik?\n";
        var expected = "User-agent: *\n" + rules + "\n"
            + "# AI arama tarayıcıları: site içeriğine açık, işlem sayfalarına kapalı.\n"
            + "User-agent: GPTBot\nUser-agent: OAI-SearchBot\nUser-agent: ClaudeBot\nUser-agent: PerplexityBot\n"
            + rules + "\n"
            + $"Sitemap: https://{hostName}/sitemap.xml\n";
        Assert.Equal(expected, body);
    }
}
