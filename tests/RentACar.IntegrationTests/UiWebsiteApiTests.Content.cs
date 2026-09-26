using System.Net;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiWebsiteApiTests
{
    // ------------------------------------------------------------------ site içeriği

    [Fact]
    public async Task Content_pages_and_faq_crud_with_version()
    {
        var e = await SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);

        var page = await Json(await Send(admin, HttpMethod.Post, V1 + "/site-icerik/sayfalar",
            new { baslik = "Hakkımızda", govde = "Biz bir kiralama firmasıyız.", sira = 1, yayinda = true }), HttpStatusCode.Created);
        var id = page.GetProperty("id").GetGuid();
        Assert.Equal("hakkimizda", page.GetProperty("slug").GetString()); // Türkçe transliterasyon
        var v1 = page.GetProperty("surum").GetString()!;

        // rezerve adres → 400 errors[slug]; boş başlık → errors[baslik]
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/site-icerik/sayfalar",
            new { baslik = "Blog", govde = "x", slug = "blog" }), HttpStatusCode.BadRequest, "dogrulama", "slug");
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/site-icerik/sayfalar",
            new { baslik = " ", govde = "x" }), HttpStatusCode.BadRequest, "dogrulama", "baslik");

        // PUT: sürüm zorunlu / doğru sürüm / bayat sürüm 409
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/site-icerik/sayfalar/{id}",
            new { baslik = "Hakkımızda", govde = "Yeni" }), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var updated = await Json(await Send(admin, HttpMethod.Put, $"{V1}/site-icerik/sayfalar/{id}",
            new { baslik = "Hakkımızda", govde = "Yeni metin", sira = 2, yayinda = true, surum = v1 }));
        Assert.Equal("Yeni metin", updated.GetProperty("govde").GetString());
        Assert.NotEqual(v1, updated.GetProperty("surum").GetString());
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/site-icerik/sayfalar/{id}",
            new { baslik = "Ezen", govde = "Bayat form", surum = v1 }), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal("Yeni metin", (await Json(await Send(admin, HttpMethod.Get, $"{V1}/site-icerik/sayfalar/{id}")))
            .GetProperty("govde").GetString());

        var hidden = await Json(await Send(admin, HttpMethod.Post, $"{V1}/site-icerik/sayfalar/{id}/durum", new { yayinda = false }));
        Assert.False(hidden.GetProperty("yayinda").GetBoolean());
        Assert.Equal(1, (await Json(await Send(admin, HttpMethod.Get, V1 + "/site-icerik/sayfalar"))).GetProperty("toplam").GetInt32());

        // SSS
        var faq = await Json(await Send(admin, HttpMethod.Post, V1 + "/site-icerik/sss",
            new { soru = "Depozito var mı?", cevap = "Evet.", sira = 1, yayinda = true }), HttpStatusCode.Created);
        var faqId = faq.GetProperty("id").GetGuid();
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/site-icerik/sss", new { soru = "", cevap = "x" }),
            HttpStatusCode.BadRequest, "dogrulama", "soru");
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/site-icerik/sss/{faqId}", new { soru = "S", cevap = "C" }),
            HttpStatusCode.BadRequest, "dogrulama", "surum");
        var faq2 = await Json(await Send(admin, HttpMethod.Put, $"{V1}/site-icerik/sss/{faqId}",
            new { soru = "Depozito alınır mı?", cevap = "Evet, 5.000 TL.", sira = 1, yayinda = true, surum = faq.GetProperty("surum").GetString() }));
        Assert.Equal("Evet, 5.000 TL.", faq2.GetProperty("cevap").GetString());
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/site-icerik/sss/{faqId}",
            new { soru = "S", cevap = "C", surum = faq.GetProperty("surum").GetString() }), HttpStatusCode.Conflict, "cakisma");

        // başka kiracı → 404; izinsiz rol → 403
        var other = await SetupAsync();
        var oa = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await Send(oa, HttpMethod.Get, $"{V1}/site-icerik/sayfalar/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(oa, HttpMethod.Delete, $"{V1}/site-icerik/sss/{faqId}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(oa, HttpMethod.Put, $"{V1}/site-icerik/sayfalar/{id}",
            new { baslik = "x", govde = "y", surum = v1 }), HttpStatusCode.NotFound, null);
        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await Send(acc, HttpMethod.Get, V1 + "/site-icerik/sss"), HttpStatusCode.Forbidden, "yetki_yok");

        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{V1}/site-icerik/sss/{faqId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{V1}/site-icerik/sayfalar/{id}")).StatusCode);
        Assert.Empty((await Json(await Send(admin, HttpMethod.Get, V1 + "/site-icerik/sss"))).EnumerateArray());
    }

    // ------------------------------------------------------------------ blog

    [Fact]
    public async Task Blog_crud_version_and_markup_is_never_html()
    {
        var e = await SetupAsync(module: false); // blog Blazor'da da modül kapısı taşımıyor
        var admin = await _kit.LoginAsync(e, Who.Admin);

        const string body = "<script>alert(1)</script>\n\n## Ara başlık\n\n<img src=x onerror=alert(1)>";
        var post = await Json(await Send(admin, HttpMethod.Post, V1 + "/blog-yonetim",
            new { baslik = "Antalya Araç Kiralama", icerik = body, durum = "Taslak", anahtarKelimeler = "antalya, Antalya, kiralık" }),
            HttpStatusCode.Created);
        var id = post.GetProperty("id").GetGuid();
        Assert.Equal("antalya-arac-kiralama", post.GetProperty("slug").GetString());
        Assert.Equal("antalya, kiralık", post.GetProperty("anahtarKelimeler").GetString()); // Türkçe-duyarlı tekrar ayıklama
        var v1 = post.GetProperty("surum").GetString()!;

        // XSS: içerik DÜZ METİN olarak saklanır ve önizleme HTML DEĞİL blok listesi döner — hiçbir yanıt alanı
        // işaretleme olarak yorumlanan bir "html" taşımaz; sitede Razor kodlar.
        var preview = await Json(await Send(admin, HttpMethod.Get, $"{V1}/blog-yonetim/{id}/onizleme"));
        var blocks = preview.GetProperty("bloklar").EnumerateArray().ToList();
        Assert.Equal(3, blocks.Count);
        Assert.Equal(("Paragraf", "<script>alert(1)</script>"), (blocks[0].GetProperty("tur").GetString(), blocks[0].GetProperty("metin").GetString()));
        Assert.Equal(("Baslik2", "Ara başlık"), (blocks[1].GetProperty("tur").GetString(), blocks[1].GetProperty("metin").GetString()));
        Assert.DoesNotContain(preview.EnumerateObject(), p => p.Name.Contains("html", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("/blog/antalya-arac-kiralama", preview.GetProperty("adres").GetString());

        // PUT: sürüm zorunlu / doğru / bayat 409; yayınlanınca slug donar
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/blog-yonetim/{id}", new { baslik = "x", icerik = "y" }),
            HttpStatusCode.BadRequest, "dogrulama", "surum");
        var pub = await Json(await Send(admin, HttpMethod.Put, $"{V1}/blog-yonetim/{id}",
            new { baslik = "Antalya Rehberi", icerik = "Metin", durum = "Yayinda", surum = v1 }));
        Assert.Equal("antalya-rehberi", pub.GetProperty("slug").GetString());
        Assert.True(pub.GetProperty("slugDondu").GetBoolean());
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/blog-yonetim/{id}",
            new { baslik = "Ezen", icerik = "Bayat", surum = v1 }), HttpStatusCode.Conflict, "cakisma");
        var renamed = await Json(await Send(admin, HttpMethod.Put, $"{V1}/blog-yonetim/{id}",
            new { baslik = "Yeni Başlık", slug = "baska-adres", icerik = "Metin", durum = "Yayinda", surum = pub.GetProperty("surum").GetString() }));
        Assert.Equal("antalya-rehberi", renamed.GetProperty("slug").GetString()); // donmuş adres

        // doğrulama → alan hataları; uzunluk sınırı uçta
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/blog-yonetim", new { baslik = "", icerik = "x" }),
            HttpStatusCode.BadRequest, "dogrulama", "baslik");
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/blog-yonetim", new { baslik = "a", icerik = "x", yazar = new string('y', 161) }),
            HttpStatusCode.BadRequest, "dogrulama", "yazar");
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/blog-yonetim", new { baslik = "a", icerik = "x", durum = "Silindi" }),
            HttpStatusCode.BadRequest, "dogrulama", "durum");

        // izinsiz rol 403, başka kiracı 404
        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await Send(acc, HttpMethod.Get, V1 + "/blog-yonetim"), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync();
        var oa = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await Send(oa, HttpMethod.Get, $"{V1}/blog-yonetim/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(oa, HttpMethod.Get, $"{V1}/blog-yonetim/{id}/onizleme"), HttpStatusCode.NotFound, null);
        await Problem(await Send(oa, HttpMethod.Delete, $"{V1}/blog-yonetim/{id}"), HttpStatusCode.NotFound, null);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{V1}/blog-yonetim/{id}")).StatusCode);
        Assert.Equal(0, (await Json(await Send(admin, HttpMethod.Get, V1 + "/blog-yonetim"))).GetProperty("toplam").GetInt32());
    }

    [Fact]
    public async Task Blog_cover_upload_is_content_sniffed_and_served_inline()
    {
        var e = await SetupAsync(module: false);
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var id = (await Json(await Send(admin, HttpMethod.Post, V1 + "/blog-yonetim",
            new { baslik = "Kapaklı yazı", icerik = "Metin" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        await Problem(await Send(admin, HttpMethod.Get, $"{V1}/blog-yonetim/{id}/kapak"), HttpStatusCode.NotFound, null);
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/blog-yonetim/{id}/kapak", Upload(FakePng(), "kapak")),
            HttpStatusCode.BadRequest, "dogrulama", "kapak");
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/blog-yonetim/{id}/kapak", Upload(Png(2 * 1024 * 1024 + 10), "kapak")),
            HttpStatusCode.BadRequest, "dogrulama", "kapak");

        var up = await Json(await Send(admin, HttpMethod.Post, $"{V1}/blog-yonetim/{id}/kapak", Upload(Png(), "kapak")),
            HttpStatusCode.Created);
        Assert.True(up.GetProperty("kapakVar").GetBoolean());

        var content = await Send(admin, HttpMethod.Get, $"{V1}/blog-yonetim/{id}/kapak");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Null(content.Content.Headers.ContentDisposition); // satır içi görsel, indirme değil
        Assert.Equal(Png(), await content.Content.ReadAsByteArrayAsync());

        var other = await SetupAsync();
        var oa = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await Send(oa, HttpMethod.Get, $"{V1}/blog-yonetim/{id}/kapak"), HttpStatusCode.NotFound, null);
        await Problem(await Send(oa, HttpMethod.Post, $"{V1}/blog-yonetim/{id}/kapak", Upload(Png(), "kapak")), HttpStatusCode.NotFound, null);

        var removed = await Json(await Send(admin, HttpMethod.Delete, $"{V1}/blog-yonetim/{id}/kapak"));
        Assert.False(removed.GetProperty("kapakVar").GetBoolean());

        // oturumsuz içerik isteği 401 JSON (yönlendirme yok)
        var anon = await fx.Web.Client().GetAsync($"{V1}/blog-yonetim/{id}/kapak");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }
}
