using System.Text;
using System.Xml.Linq;
using RentACar.Application.Blog;
using RentACar.Application.Fleet;

namespace RentACar.PublicSite;

/// <summary>
/// PR-9: `robots.txt` + `sitemap.xml`. İKİSİ DE `GetCanonicalHostAsync`'ten okur — `req.Host`'TAN DEĞİL.
/// Neden: ziyaretçi subdomain'den gelse bile tenant'ın canonical'i özel domain olabilir; sitemap
/// subdomain'i gösterirse sayfalardaki `canonical` etiketiyle ÇELİŞİR (kendi kendini çürüten SEO sinyali).
/// Bu yüzden hangi host'tan istenirse istensin AYNI (kanonik) URL'ler üretilir.
/// </summary>
public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", async (FleetShowcaseService showcase, CancellationToken ct) =>
        {
            var host = await showcase.GetCanonicalHostAsync(ct);
            var sb = new StringBuilder()
                .AppendLine("User-agent: *")
                .AppendLine("Allow: /");
            if (host is not null) sb.AppendLine($"Sitemap: https://{host}/sitemap.xml");
            return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
        });

        app.MapGet("/sitemap.xml", async (FleetShowcaseService showcase, BlogService blog, CancellationToken ct) =>
        {
            var host = await showcase.GetCanonicalHostAsync(ct);
            if (host is null) return Results.NotFound(); // site hiç yayında değil

            var kok = $"https://{host}";
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var urls = new List<XElement> { Url(ns, kok + "/") };

            foreach (var g in await showcase.ListShowcaseGroupsAsync(ct))
                urls.Add(Url(ns, $"{kok}/araclar/{g.GroupId}"));

            var yazilar = await blog.ListPublishedAsync(ct);
            if (yazilar.Count > 0)
            {
                urls.Add(Url(ns, kok + "/blog"));
                foreach (var y in yazilar)
                    urls.Add(Url(ns, $"{kok}/blog/{y.Slug}", y.YayinTarihi));
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(ns + "urlset", urls));
            return Results.Text(doc.ToString(), "application/xml; charset=utf-8");
        });

        return app;
    }

    private static XElement Url(XNamespace ns, string loc, DateTimeOffset? lastMod = null)
    {
        var el = new XElement(ns + "url", new XElement(ns + "loc", loc));
        if (lastMod is { } t) el.Add(new XElement(ns + "lastmod", t.UtcDateTime.ToString("yyyy-MM-dd")));
        return el;
    }
}
