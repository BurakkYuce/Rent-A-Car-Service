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

            var kok = $"https://{host}";
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var urls = new List<XElement> { Url(ns, kok + "/") };

            // PR-14: adres artık SLUG (eski `/araclar/{groupId}` GUID'leri yerine).
            foreach (var g in await showcase.ListShowcaseGroupsAsync(ct))
                urls.Add(Url(ns, $"{kok}/araclar/{g.Slug}"));

            // PR-16: iletişim her zaman var (kod üretimli); SSS ve içerik sayfaları varsa eklenir.
            urls.Add(Url(ns, kok + "/iletisim"));
            // Müsaitlik sayfası sitemap'te YOKTU: ziyaretçinin asıl aradığı yüzey (tarih + fiyat)
            // ve tamamen indekslenebilir statik SSR. Ana sayfadan link var ama sitemap'te
            // olmaması onu ikinci sınıf bir sayfa gibi gösteriyordu.
            urls.Add(Url(ns, kok + "/musaitlik"));
            if ((await icerik.PublishedFaqAsync(ct)).Count > 0) urls.Add(Url(ns, kok + "/sss"));
            foreach (var sf in await icerik.PublishedPagesAsync(ct))
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

            var kok = $"https://{host}";
            var b = await showcase.GetBrandingAsync(ct);
            var marka = string.IsNullOrWhiteSpace(b.Marka) ? host : b.Marka!;
            var sb = new StringBuilder();

            sb.AppendLine($"# {marka}");
            sb.AppendLine();
            sb.AppendLine($"> {marka} — araç kiralama (rent a car). Müsaitlik ve günlük fiyatlar site üzerinden");
            sb.AppendLine("> sorgulanır, rezervasyon talebi çevrimiçi formla iletilir.");
            sb.AppendLine();
            sb.AppendLine("Bu dosya llmstxt.org konvansiyonuna göre üretilmiştir ve firmanın yayındaki");
            sb.AppendLine("araç ilanlarını, blog yazılarını ve ana sayfalarını özetler. Veriler istek anında üretilir.");

            // İletişim: yalnız DOLU alanlar — boş satır ("Telefon: ") ajanı yanıltır, hiç yazmamak dürüsttür.
            var iletisim = new List<string>();
            Ekle(iletisim, "Telefon", b.Tel);
            Ekle(iletisim, "Mobil telefon", b.MobilTel);
            Ekle(iletisim, "WhatsApp", b.WhatsApp);
            Ekle(iletisim, "E-posta", b.Email);
            Ekle(iletisim, "Adres", b.Adres);
            iletisim.Add($"- Web: {kok}");
            sb.AppendLine().AppendLine("## İletişim").AppendLine();
            foreach (var s in iletisim) sb.AppendLine(s);

            // Şubeler = hizmet lokasyonları. `WebIsim` varsa o kazanır: kurumsal ad ile sitede
            // gösterilen ad kasıtlı olarak farklı olabilir (Branch.WebIsim'in var oluş nedeni).
            var aktifSubeler = await subeler.ListActiveAsync(ct);
            if (aktifSubeler.Count > 0)
            {
                sb.AppendLine().AppendLine("## Şubeler / Hizmet Lokasyonları").AppendLine();
                foreach (var s in aktifSubeler)
                {
                    var ad = string.IsNullOrWhiteSpace(s.WebIsim) ? s.Ad : s.WebIsim!;
                    var yer = string.Join(" / ", new[] { s.Il, s.Ilce }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    var ek = new[] { yer, s.Adres, s.Telefon }.Where(x => !string.IsNullOrWhiteSpace(x));
                    sb.AppendLine($"- {ad}{(ek.Any() ? " — " + string.Join(" — ", ek) : "")}");
                }
            }

            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var ilanlar = await showcase.ListShowcaseGroupsAsync(ct);
            sb.AppendLine().AppendLine($"## Kiralanabilir Araçlar ({ilanlar.Count} ilan)").AppendLine();
            if (ilanlar.Count == 0)
            {
                sb.AppendLine($"- Şu an yayında ilan yok. Güncel filo: {kok}/musaitlik");
            }
            else
            {
                // Fiyat biçimi tr-TR ile SABİTLENİR: sunucunun ambient kültürü ortama göre değişir
                // (CI/konteyner çoğu zaman invariant) ve "1.500" ile "1,500" arasında gidip gelmek
                // ajanın okuduğu rakamı bozar. Adet = vitrindeki gösterim adedi, kapasite değil.
                foreach (var i in ilanlar)
                {
                    var yil = string.IsNullOrWhiteSpace(i.YilAralik) ? "" : $" ({i.YilAralik})";
                    var kdv = i.KdvDahil ? "KDV dahil" : "KDV hariç";
                    sb.AppendLine($"- {i.Baslik}{yil} — {i.GunlukFiyat.ToString("N0", tr)} TL/gün ({kdv}) — "
                        + $"{i.Adet} adet: {kok}/araclar/{i.Slug}");
                }
                sb.AppendLine($"- Tarihe göre müsaitlik ve toplam fiyat: {kok}/musaitlik");
            }

            var yazilar = await blog.ListPublishedAsync(ct);
            if (yazilar.Count > 0)
            {
                sb.AppendLine().AppendLine($"## Blog / Rehber ({yazilar.Count} yazı)").AppendLine();
                foreach (var y in yazilar)
                    sb.AppendLine($"- {y.Baslik}: {kok}/blog/{y.Slug}");
            }

            sb.AppendLine().AppendLine("## Önemli Sayfalar").AppendLine();
            sb.AppendLine($"- Ana sayfa: {kok}/");
            sb.AppendLine($"- Müsaitlik ve fiyat sorgulama: {kok}/musaitlik");
            sb.AppendLine($"- Rezervasyon talebi: {kok}/rezervasyon-talebi");
            if (yazilar.Count > 0) sb.AppendLine($"- Blog: {kok}/blog");
            sb.AppendLine($"- İletişim: {kok}/iletisim");
            // SSS ve serbest içerik sayfaları YAYINDAYSA listelenir — sitemap ile aynı kapı.
            if ((await icerik.PublishedFaqAsync(ct)).Count > 0) sb.AppendLine($"- Sık sorulan sorular: {kok}/sss");
            foreach (var sf in await icerik.PublishedPagesAsync(ct))
                sb.AppendLine($"- {sf.Baslik}: {kok}/{sf.Slug}");

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
    private static void Ekle(List<string> hedef, string etiket, string? deger)
    {
        if (!string.IsNullOrWhiteSpace(deger)) hedef.Add($"- {etiket}: {deger.Trim()}");
    }

    private static XElement Url(XNamespace ns, string loc, DateTimeOffset? lastMod = null)
    {
        var el = new XElement(ns + "url", new XElement(ns + "loc", loc));
        if (lastMod is { } t) el.Add(new XElement(ns + "lastmod", t.UtcDateTime.ToString("yyyy-MM-dd")));
        return el;
    }
}
