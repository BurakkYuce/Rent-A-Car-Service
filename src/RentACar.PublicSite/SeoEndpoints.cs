using System.Globalization;
using System.Text;
using System.Xml.Linq;
using RentACar.Application.Blog;
using RentACar.Application.Branches;
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
            RentACar.Application.SiteIcerik.SiteContentService icerik, CancellationToken ct) =>
        {
            var host = await showcase.GetCanonicalHostAsync(ct);
            if (host is null) return Results.NotFound(); // site hiç yayında değil

            var root = $"https://{host}";
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var urls = new List<XElement> { Url(ns, root + "/") };

            // PR-14: adres artık SLUG (eski `/araclar/{groupId}` GUID'leri yerine).
            foreach (var g in await showcase.ListShowcaseGroupsAsync(ct))
                urls.Add(Url(ns, $"{root}/araclar/{g.Slug}"));

            // PR-16: iletişim her zaman var (kod üretimli); SSS ve içerik sayfaları varsa eklenir.
            urls.Add(Url(ns, root + "/iletisim"));
            // Müsaitlik sayfası sitemap'te YOKTU: ziyaretçinin asıl aradığı yüzey (tarih + fiyat)
            // ve tamamen indekslenebilir statik SSR. Ana sayfadan link var ama sitemap'te
            // olmaması onu ikinci sınıf bir sayfa gibi gösteriyordu.
            urls.Add(Url(ns, root + "/musaitlik"));
            if ((await icerik.PublishedFaqAsync(ct)).Count > 0) urls.Add(Url(ns, root + "/sss"));
            foreach (var sf in await icerik.PublishedPagesAsync(ct))
                urls.Add(Url(ns, $"{root}/{sf.Slug}"));

            var posts = await blog.ListPublishedAsync(ct);
            if (posts.Count > 0)
            {
                urls.Add(Url(ns, root + "/blog"));
                foreach (var y in posts)
                    urls.Add(Url(ns, $"{root}/blog/{y.Slug}", y.YayinTarihi));
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(ns + "urlset", urls));
            return Results.Text(doc.ToString(), "application/xml; charset=utf-8");
        });

        // `/llms.txt` — llmstxt.org konvansiyonu: AI arama motorları / LLM ajanları için sade metin
        // site özeti. Neden HTML kazımak yerine ayrı bir uç: ajan tek istekte firmayı, iletişimi,
        // yayındaki filoyu ve blogu MUTLAK adreslerle görsün — vitrin sayfaları static-SSR olsa da
        // fiyat/adet bilgisi kart HTML'ine gömülü ve kazıma sırasında bayatlamaya açık.
        // Host `GetCanonicalHostAsync`'ten: robots/sitemap ile AYNI kural (bkz. sınıf özeti) —
        // ajanın alıntıladığı adres, sayfalardaki `canonical` etiketiyle çelişmemeli.
        app.MapGet("/llms.txt", async (FleetShowcaseService showcase, BlogService blog,
            BranchService subeler, RentACar.Application.SiteIcerik.SiteContentService icerik,
            CancellationToken ct) =>
        {
            var host = await showcase.GetCanonicalHostAsync(ct);
            if (host is null) return Results.NotFound(); // site hiç yayında değil — sitemap ile aynı davranış

            var root = $"https://{host}";
            var b = await showcase.GetBrandingAsync(ct);
            var brand = string.IsNullOrWhiteSpace(b.Marka) ? host : b.Marka!;
            var sb = new StringBuilder();

            sb.AppendLine($"# {brand}");
            sb.AppendLine();
            sb.AppendLine($"> {brand} — araç kiralama (rent a car). Müsaitlik ve günlük fiyatlar site üzerinden");
            sb.AppendLine("> sorgulanır, rezervasyon talebi çevrimiçi formla iletilir.");
            sb.AppendLine();
            sb.AppendLine("Bu dosya llmstxt.org konvansiyonuna göre üretilmiştir ve firmanın yayındaki");
            sb.AppendLine("araç ilanlarını, blog yazılarını ve ana sayfalarını özetler. Veriler istek anında üretilir.");

            // İletişim: yalnız DOLU alanlar — boş satır ("Telefon: ") ajanı yanıltır, hiç yazmamak dürüsttür.
            var contact = new List<string>();
            Add(contact, "Telefon", b.Tel);
            Add(contact, "Mobil telefon", b.MobilTel);
            Add(contact, "WhatsApp", b.WhatsApp);
            Add(contact, "E-posta", b.Email);
            Add(contact, "Adres", b.Adres);
            contact.Add($"- Web: {root}");
            sb.AppendLine().AppendLine("## İletişim").AppendLine();
            foreach (var s in contact) sb.AppendLine(s);

            // Şubeler = hizmet lokasyonları. `WebIsim` varsa o kazanır: kurumsal ad ile sitede
            // gösterilen ad kasıtlı olarak farklı olabilir (Branch.WebIsim'in var oluş nedeni).
            var activeBranches = await subeler.ListActiveAsync(ct);
            if (activeBranches.Count > 0)
            {
                sb.AppendLine().AppendLine("## Şubeler / Hizmet Lokasyonları").AppendLine();
                foreach (var s in activeBranches)
                {
                    var name = string.IsNullOrWhiteSpace(s.WebIsim) ? s.Ad : s.WebIsim!;
                    var place = string.Join(" / ", new[] { s.Il, s.Ilce }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    var extra = new[] { place, s.Adres, s.Telefon }.Where(x => !string.IsNullOrWhiteSpace(x));
                    sb.AppendLine($"- {name}{(extra.Any() ? " — " + string.Join(" — ", extra) : "")}");
                }
            }

            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var listings = await showcase.ListShowcaseGroupsAsync(ct);
            sb.AppendLine().AppendLine($"## Kiralanabilir Araçlar ({listings.Count} ilan)").AppendLine();
            if (listings.Count == 0)
            {
                sb.AppendLine($"- Şu an yayında ilan yok. Güncel filo: {root}/musaitlik");
            }
            else
            {
                // Fiyat biçimi tr-TR ile SABİTLENİR: sunucunun ambient kültürü ortama göre değişir
                // (CI/konteyner çoğu zaman invariant) ve "1.500" ile "1,500" arasında gidip gelmek
                // ajanın okuduğu rakamı bozar. Adet = vitrindeki gösterim adedi, kapasite değil.
                foreach (var i in listings)
                {
                    var year = string.IsNullOrWhiteSpace(i.YilAralik) ? "" : $" ({i.YilAralik})";
                    var vat = i.KdvDahil ? "KDV dahil" : "KDV hariç";
                    sb.AppendLine($"- {i.Baslik}{year} — {i.GunlukFiyat.ToString("N0", tr)} TL/gün ({vat}) — "
                        + $"{i.Adet} adet: {root}/araclar/{i.Slug}");
                }
                sb.AppendLine($"- Tarihe göre müsaitlik ve toplam fiyat: {root}/musaitlik");
            }

            var posts = await blog.ListPublishedAsync(ct);
            if (posts.Count > 0)
            {
                sb.AppendLine().AppendLine($"## Blog / Rehber ({posts.Count} yazı)").AppendLine();
                foreach (var y in posts)
                    sb.AppendLine($"- {y.Baslik}: {root}/blog/{y.Slug}");
            }

            sb.AppendLine().AppendLine("## Önemli Sayfalar").AppendLine();
            sb.AppendLine($"- Ana sayfa: {root}/");
            sb.AppendLine($"- Müsaitlik ve fiyat sorgulama: {root}/musaitlik");
            sb.AppendLine($"- Rezervasyon talebi: {root}/rezervasyon-talebi");
            if (posts.Count > 0) sb.AppendLine($"- Blog: {root}/blog");
            sb.AppendLine($"- İletişim: {root}/iletisim");
            // SSS ve serbest içerik sayfaları YAYINDAYSA listelenir — sitemap ile aynı kapı.
            if ((await icerik.PublishedFaqAsync(ct)).Count > 0) sb.AppendLine($"- Sık sorulan sorular: {root}/sss");
            foreach (var sf in await icerik.PublishedPagesAsync(ct))
                sb.AppendLine($"- {sf.Baslik}: {root}/{sf.Slug}");

            return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
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

    /// <summary>Boş/whitespace değeri hiç yazmaz — eksik alanı boş satırla göstermek ajanı yanıltır.</summary>
    private static void Add(List<string> target, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target.Add($"- {label}: {value.Trim()}");
    }

    private static XElement Url(XNamespace ns, string loc, DateTimeOffset? lastMod = null)
    {
        var el = new XElement(ns + "url", new XElement(ns + "loc", loc));
        if (lastMod is { } t) el.Add(new XElement(ns + "lastmod", t.UtcDateTime.ToString("yyyy-MM-dd")));
        return el;
    }
}
