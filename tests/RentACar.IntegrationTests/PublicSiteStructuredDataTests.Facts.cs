using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Blog;
using RentACar.Application.SiteIcerik;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class PublicSiteStructuredDataTests
{
    [Fact]
    public async Task AutoRental_veriden_zenginlesir_ve_ana_sayfa_FAQ_gorunen_kadar()
    {
        var (t, root) = await SeedTenantAsync();
        using (var host = new TestHost(fx.AppConnectionString))
        {
            await SeedFirmAsync(host, t);
            await SeedBranchAsync(host, t, "Mo-Sa 09:00-19:00");
            await SeedListingAsync(host, t);
            using var s = host.ScopeFor(t);
            var content = s.ServiceProvider.GetRequiredService<SiteContentService>();
            for (var i = 1; i <= 6; i++)
                await content.SaveFaqAsync(new SssInput(null, $"Soru {i}?", $"Cevap {i}.", i, true));
        }

        using var factory = new PublicSiteFactory(fx, t);
        var html = await factory.Client().GetStringAsync("/");

        var biz = Node(html, "AutoRental");
        Assert.Equal(root + "/#firma", Str(biz, "@id"));
        Assert.Equal(root + "/", Str(biz, "url"));
        Assert.Equal(Brand, Str(biz, "name"));
        Assert.StartsWith(root + "/marka/logo?v=", Str(biz, "logo"));
        Assert.Equal("₺1.500", Str(biz, "priceRange"));                 // tek ilan → tek fiyat
        Assert.Equal("Mo-Sa 09:00-19:00", Str(biz, "openingHours"));
        var geo = biz.GetProperty("geo");
        Assert.Equal(38.4192m, geo.GetProperty("latitude").GetDecimal());
        Assert.Equal(27.1287m, geo.GetProperty("longitude").GetDecimal());
        var address = biz.GetProperty("address");
        Assert.Equal("İzmir", Str(address, "addressRegion"));
        Assert.Equal("Konak", Str(address, "addressLocality"));
        Assert.False(biz.TryGetProperty("sameAs", out _));                // veri modeli yok → basılmaz

        // L-4: FAQPage soru sayısı == sayfada görünen <details> sayısı (ana sayfa özeti 4).
        var faq = Node(html, "FAQPage");
        Assert.Equal(4, faq.GetProperty("mainEntity").GetArrayLength());
        Assert.Equal(4, Regex.Matches(html, @"<details class=""sss""").Count);
    }

    [Fact]
    public async Task Gunsuz_calisma_saati_ve_koordinatsiz_sube_uydurulmaz()
    {
        var (t, _) = await SeedTenantAsync();
        using (var host = new TestHost(fx.AppConnectionString))
        {
            await SeedFirmAsync(host, t);
            await SeedBranchAsync(host, t, "09:00-18:00"); // günler yazılmamış → schema biçimi değil
        }

        using var factory = new PublicSiteFactory(fx, t);
        var biz = Node(await factory.Client().GetStringAsync("/"), "AutoRental");
        Assert.False(biz.TryGetProperty("openingHours", out _));
        Assert.False(biz.TryGetProperty("priceRange", out _));            // yayında ilan yok
    }

    [Fact]
    public async Task Ilan_detayi_Car_ve_Offer_ilan_verisinden()
    {
        var (t, root) = await SeedTenantAsync();
        string slug;
        using (var host = new TestHost(fx.AppConnectionString))
        {
            await SeedFirmAsync(host, t);
            slug = await SeedListingAsync(host, t);
        }

        using var factory = new PublicSiteFactory(fx, t);
        var html = await factory.Client().GetStringAsync("/araclar/" + slug);

        var car = Node(html, "Car");
        Assert.Equal($"{root}/araclar/{slug}", Str(car, "url"));
        Assert.Equal("Fiat", Str(car.GetProperty("brand"), "name"));
        Assert.Equal("Manuel", Str(car, "vehicleTransmission"));
        Assert.Equal("2023", Str(car, "vehicleModelDate"));
        Assert.All(car.GetProperty("image").EnumerateArray(), i => Assert.StartsWith(root + "/foto/", i.GetString()));
        var offer = car.GetProperty("offers");
        Assert.Equal(1500m, offer.GetProperty("price").GetDecimal());
        Assert.Equal("TRY", Str(offer, "priceCurrency"));
        Assert.Equal(root + "/#firma", Str(offer.GetProperty("seller"), "@id"));
        Assert.False(offer.TryGetProperty("availability", out _));         // müsaitlik tarihe bağlı — yazılmaz

        var crumbs = Node(html, "BreadcrumbList").GetProperty("itemListElement");
        Assert.Equal(3, crumbs.GetArrayLength());
        Assert.Equal($"{root}/araclar/{slug}", Str(crumbs[2], "item"));
    }

    /// <summary>H-3: og:url ve JSON-LD adresleri istek host'undan (localhost) DEĞİL, kanonik kökten; og:url
    /// &lt;link rel=canonical&gt; ile birebir aynı.</summary>
    [Fact]
    public async Task Og_url_ve_JSONLD_adresleri_kanonik_kokten_ve_canonical_ile_ayni()
    {
        var (t, root) = await SeedTenantAsync();
        string postSlug;
        using (var host = new TestHost(fx.AppConnectionString))
        {
            await SeedFirmAsync(host, t);
            using var s = host.ScopeFor(t);
            await s.ServiceProvider.GetRequiredService<SiteContentService>()
                .SaveFaqAsync(new SssInput(null, "Depozito var mı?", "Firmaya sorun.", 1, true));
            var blog = s.ServiceProvider.GetRequiredService<BlogService>();
            var postId = await blog.CreateAsync(new BlogInput { Baslik = "Yol rehberi", Icerik = "Gövde.", Durum = BlogPostDurum.Yayinda });
            postSlug = (await blog.ListPublishedAsync()).Single(p => p.Id == postId).Slug;
        }

        using var factory = new PublicSiteFactory(fx, t);
        var client = factory.Client();
        foreach (var path in new[] { "/sss", "/musaitlik", "/blog/" + postSlug })
        {
            var html = await client.GetStringAsync(path);
            Assert.DoesNotContain("localhost", html);
            var canonical = Attr(html, @"<link rel=""canonical"" href=""([^""]+)""");
            var ogUrl = Attr(html, @"<meta property=""og:url"" content=""([^""]+)""");
            Assert.Equal(root + path, canonical);
            Assert.Equal(canonical, ogUrl);
        }

        var sss = await client.GetStringAsync("/sss");
        Assert.Equal(root + "/", Str(Node(sss, "AutoRental"), "url"));      // işletme = site kökü, "/sss" değil

        var post = Node(await client.GetStringAsync("/blog/" + postSlug), "BlogPosting");
        Assert.Equal($"{root}/blog/{postSlug}", Str(post, "mainEntityOfPage"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", Str(post, "dateModified")!);
        Assert.Equal(root + "/#firma", Str(post.GetProperty("publisher"), "@id"));
        Assert.StartsWith(root + "/marka/logo?v=", Str(post.GetProperty("publisher").GetProperty("logo"), "url"));
    }
}
