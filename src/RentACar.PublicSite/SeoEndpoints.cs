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

        app.MapGet("/sitemap.xml", async (FleetShowcaseService showcase, BlogService blog,
            RentACar.Application.SiteIcerik.SiteIcerikService icerik, CancellationToken ct) =>
        {
            var host = await showcase.GetCanonicalHostAsync(ct);
            if (host is null) return Results.NotFound(); // site hiç yayında değil

            var kok = $"https://{host}";
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var urls = new List<XElement> { Url(ns, kok + "/") };

            // PR-14: adres artık SLUG (eski `/araclar/{groupId}` GUID'leri yerine).
            foreach (var g in await showcase.ListShowcaseGroupsAsync(ct))
                urls.Add(Url(ns, $"{kok}/araclar/{g.Slug}"));

            // PR-16: iletişim her zaman var (kod üretimli); SSS ve içerik sayfaları varsa eklenir.
            urls.Add(Url(ns, kok + "/iletisim"));
            if ((await icerik.YayindakiSssAsync(ct)).Count > 0) urls.Add(Url(ns, kok + "/sss"));
            foreach (var sf in await icerik.YayindakiSayfalarAsync(ct))
                urls.Add(Url(ns, $"{kok}/{sf.Slug}"));

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

        // PR-14 GEÇİŞ: eski `/araclar/{guid}` adresleri. Google bunları indeksledi ve blog
        // içeriğinde elle yazılmış linkler olabilir — hepsini 404'e düşürmek yerine slug'a
        // KALICI (301) yönlendiriyoruz. GUID bir ilana çözülmezse (eski GRUP id'si ya da
        // yayından kalkmış ilan) vitrine 302 — ziyaretçi boş sayfada kalmasın.
        // Rota, slug sayfasından DAHA SPESİFİK olduğu için (`:guid` kısıtı) onunla çakışmaz.
        app.MapGet("/araclar/{id:guid}", async (Guid id, FleetShowcaseService showcase, CancellationToken ct) =>
        {
            var slug = await showcase.SlugByIdAsync(id, ct);
            return slug is null
                ? Results.Redirect("/", permanent: false)
                : Results.Redirect($"/araclar/{slug}", permanent: true);
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
