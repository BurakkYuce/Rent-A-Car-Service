using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.SiteIcerik;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-16 — halka açık site içerik sayfaları + SSS.
///
/// <para><b>EN KRİTİK TEST:</b> <c>Rezerve_slug_REDDEDILIR</c>. Sayfalar KÖK adreste yaşıyor
/// (<c>/hakkimizda</c>); ASP.NET yönlendirmesinde literal segment parametreliyi yendiği için
/// <c>blog</c> slug'lı bir sayfa kabul edilirse firmanın sayfası SESSİZCE hiç açılmaz — kullanıcı
/// "kaydettim ama görünmüyor" der ve sebebi hiçbir yerde yazmaz. Çit yazma anında.</para>
///
/// Bağımsız oracle: beklenenler senaryodan kurulur ("yayından aldım → sitede görünmemeli"),
/// servisin kendi yükleminden türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class SiteIcerikTests(PostgresFixture fx)
{
    private static SiteContentService Svc(TestHost host, Guid tenantId, out IServiceScope scope,
        UserRole role = UserRole.Admin)
    {
        scope = host.ScopeFor(tenantId, role: role);
        return scope.ServiceProvider.GetRequiredService<SiteContentService>();
    }

    private static SayfaIcerikInput Girdi(string baslik = "Hakkımızda", string govde = "Birinci paragraf.\n\nİkinci paragraf.",
        string? slug = null, bool yayinda = true, int sira = 0, Guid? id = null)
        => new(id, baslik, govde, slug, null, sira, yayinda);

    // ---- Slug üretimi ve çitler ----

    [Fact]
    public async Task Slug_baslıktan_TURKCE_duyarli_uretilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            await svc.SaveAsync(Girdi("Şoför Kılavuzu ve İç Düzen"));
            var s = Assert.Single(await svc.ListAsync());
            // ToLowerInvariant "İ"yi kaçırırdı; TurkishText.Slugify doğru indirger.
            Assert.Equal("sofor-kilavuzu-ve-ic-duzen", s.Slug);
        }
    }

    [Theory]
    [InlineData("blog")]
    [InlineData("musaitlik")]
    [InlineData("araclar")]
    [InlineData("iletisim")]
    [InlineData("sss")]
    [InlineData("Blog")]      // slug'lanınca "blog" olur → yine reddedilmeli
    public async Task Rezerve_slug_REDDEDILIR(string slug)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(
                () => svc.SaveAsync(Girdi("Deneme", slug: slug)));
            Assert.Contains("sistem tarafından kullanılıyor", ex.Message);
            Assert.Empty(await svc.ListAsync());   // hiç kayıt açılmadı
        }
    }

    /// <summary>
    /// Rezerve liste ile PublicSite'ın gerçek kök rotaları örtüşmeli (liste elle tutuluyor; bu test
    /// bugün bilinenleri kilitler).
    ///
    /// <para>NOKTALI rotalar (<c>robots.txt</c>, <c>sitemap.xml</c>) listede YOK ve olmamalı: slug
    /// üretimi noktayı düşürdüğü için onlarla çakışan bir slug ÜRETİLEMEZ. Bu testin ikinci yarısı
    /// tam olarak o değişmezi kilitliyor — listeye "ihtiyaten" eklenmiş ölü girişler geri gelmesin.</para>
    /// </summary>
    [Fact]
    public void Rezerve_slug_listesi_kok_rotalarla_ORTUSUR_noktali_rotalar_HARIC()
    {
        string[] slugSekilliKokRotalar =
        [
            "araclar", "blog", "blog-kapak", "cok-istek", "dogrulama", "foto", "iletisim",
            "musaitlik", "not-found", "rezervasyon-talebi", "sss", "talep-alindi",
        ];
        Assert.All(slugSekilliKokRotalar, r => Assert.Contains(r, SiteContentService.ReservedSlugs));

        // DEĞİŞMEZ: hiçbir slug nokta içeremez → noktalı rotalar gölgelenemez.
        Assert.All(["robots.txt", "sitemap.xml", "a.b.c"],
            r => Assert.DoesNotContain('.', TurkishText.Slugify(r)));
        Assert.DoesNotContain(SiteContentService.ReservedSlugs, r => r.Contains('.'));
    }

    [Fact]
    public async Task Ayni_slug_IKI_sayfada_olamaz_ama_kendini_saymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            var id = await svc.SaveAsync(Girdi("Hakkımızda"));
            await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(Girdi("Hakkımızda")));

            // Aynı sayfayı yeniden kaydetmek (slug değişmedi) SORUN DEĞİL.
            await svc.SaveAsync(Girdi("Hakkımızda", govde: "Güncellendi.", id: id));
            var s = Assert.Single(await svc.ListAsync());
            Assert.Equal("hakkimizda", s.Slug);
        }
    }

    [Fact]
    public async Task Bos_baslik_bos_govde_ve_uzun_metin_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(Girdi("   ")));
            await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(Girdi(govde: "  ")));
            await Assert.ThrowsAsync<ValidationException>(
                () => svc.SaveAsync(Girdi(govde: new string('x', SiteContentService.MaxBody + 1))));
            // Slug'lanınca hiçbir harf kalmayan başlık: adres üretilemez.
            await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(Girdi("!!! ???")));
        }
    }

    // ---- Yayın durumu: sitede görünürlük ----

    [Fact]
    public async Task Yayindan_alinan_sayfa_SITEDE_YOK_ama_yonetimde_DURUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            var id = await svc.SaveAsync(Girdi("Kiralama Koşulları", yayinda: true));
            Assert.NotNull(await svc.PageAsync("kiralama-kosullari"));
            Assert.Single(await svc.PublishedPagesAsync());

            await svc.PublishStatusAsync(id, published: false);

            Assert.Null(await svc.PageAsync("kiralama-kosullari"));   // uç 404 döner
            Assert.Empty(await svc.PublishedPagesAsync());          // footer/sitemap'te yok
            Assert.Single(await svc.ListAsync());                   // …ama yönetimde duruyor
        }
    }

    [Fact]
    public async Task Silinen_sayfa_hem_yonetimden_hem_siteden_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            var id = await svc.SaveAsync(Girdi("Geçici"));
            await svc.DeleteAsync(id);
            Assert.Empty(await svc.ListAsync());
            Assert.Null(await svc.PageAsync("gecici"));
            await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(id)); // ikinci silme
        }
    }

    // ---- İZOLASYON ----

    [Fact]
    public async Task Baska_tenantin_sayfasi_SLUG_ile_de_gorunmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        var a = Svc(host, t1, out var s1); using (s1)
            await a.SaveAsync(Girdi("Hakkımızda", govde: "T1 metni."));

        var b = Svc(host, t2, out var s2); using (s2)
        {
            // Aynı slug'ı BİLSE bile göremez (EF filter + RLS) — üstelik kendisi de aynı slug'ı açabilir.
            Assert.Null(await b.PageAsync("hakkimizda"));
            Assert.Empty(await b.ListAsync());
            await b.SaveAsync(Girdi("Hakkımızda", govde: "T2 metni."));
            Assert.Equal("T2 metni.", (await b.PageAsync("hakkimizda"))!.Govde);

            // racar_app + FORCE RLS: filtre kaldırılsa DA yabancı satır gelmez.
            var f = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var ctx = await f.CreateDbContextAsync();
            Assert.Single(await ctx.SayfaIcerikler.IgnoreQueryFilters().ToListAsync());
        }

        // T1'in metni bozulmadı.
        var a2 = Svc(host, t1, out var s3); using (s3)
            Assert.Equal("T1 metni.", (await a2.PageAsync("hakkimizda"))!.Govde);
    }

    // ---- SSS ----

    [Fact]
    public async Task Sss_yayin_filtresi_yonetimde_HEPSI_sitede_YALNIZ_yayinda()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            await svc.SaveFaqAsync(new SssInput(null, "Depozito var mı?", "Evet.", 1, true));
            await svc.SaveFaqAsync(new SssInput(null, "Taslak soru", "Henüz hazır değil.", 2, false));

            Assert.Equal(2, (await svc.ListFaqAsync()).Count);              // yönetim
            var yayin = Assert.Single(await svc.PublishedFaqAsync());        // site
            Assert.Equal("Depozito var mı?", yayin.Soru);
        }
    }

    [Fact]
    public async Task Sss_sira_ile_sıralanır_ve_bos_alan_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            await svc.SaveFaqAsync(new SssInput(null, "İkinci", "B", 2, true));
            await svc.SaveFaqAsync(new SssInput(null, "Birinci", "A", 1, true));
            Assert.Equal(["Birinci", "İkinci"], (await svc.PublishedFaqAsync()).Select(x => x.Soru));

            await Assert.ThrowsAsync<ValidationException>(() => svc.SaveFaqAsync(new SssInput(null, " ", "A", 0, true)));
            await Assert.ThrowsAsync<ValidationException>(() => svc.SaveFaqAsync(new SssInput(null, "S", " ", 0, true)));
        }
    }

    // ---- Yetki ----

    [Fact]
    public async Task Operasyon_yetkisi_olmayan_rol_ICERIK_YONETEMEZ_ama_site_OKUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        var admin = Svc(host, tenantId, out var s1); using (s1)
            await admin.SaveAsync(Girdi("Hakkımızda"));

        var muhasebe = Svc(host, tenantId, out var s2, UserRole.Muhasebe); using (s2)
        {
            await Assert.ThrowsAsync<NoPermissionException>(() => muhasebe.SaveAsync(Girdi("Yeni")));
            await Assert.ThrowsAsync<NoPermissionException>(() => muhasebe.ListAsync());

            // OKUMA yolu guard'sız: halka açık siteyi anonim ziyaretçi çağırıyor, orada rol yok.
            Assert.NotNull(await muhasebe.PageAsync("hakkimizda"));
            Assert.Single(await muhasebe.PublishedPagesAsync());
        }
    }

    // ---- Render kuralı (XSS sınıfı) ----

    /// <summary>
    /// Gövde HTML olarak DEĞİL düz metin olarak saklanır ve paragraflara bölünür. Razor bu parçaları
    /// otomatik encode ediyor (<c>MarkupString</c> kullanılmıyor) → <c>&lt;script&gt;</c> ekranda
    /// METİN görünür. Test, saklanan değerin bozulmadığını ve bölme kuralını kilitler.
    /// </summary>
    [Fact]
    public async Task Govde_HTML_olarak_yorumlanmaz_paragraflara_bolunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = Svc(host, Guid.NewGuid(), out var scope); using (scope)
        {
            const string kotu = "<script>alert(1)</script>\n\nİkinci <b>paragraf</b>.";
            await svc.SaveAsync(Girdi("Deneme", govde: kotu));

            var s = await svc.PageAsync("deneme");
            Assert.Equal(kotu, s!.Govde);                       // ham metin bozulmadan saklandı

            var p = ContentText.Paragraphs(s.Govde);
            Assert.Equal(2, p.Count);
            Assert.Equal("<script>alert(1)</script>", p[0]);    // etiket METİN olarak taşınıyor
            Assert.Equal("İkinci <b>paragraf</b>.", p[1]);
        }
    }

    [Fact]
    public void Paragraflar_bos_girdiyi_ve_CRLF_yi_dogru_isler()
    {
        Assert.Empty(ContentText.Paragraphs(null));
        Assert.Empty(ContentText.Paragraphs("   "));
        Assert.Equal(["A", "B"], ContentText.Paragraphs("A\r\n\r\nB"));
        Assert.Equal(["A", "B"], ContentText.Paragraphs("\n\nA\n\n\n\nB\n\n"));
        Assert.Equal(["Tek satır"], ContentText.Paragraphs("Tek satır"));
    }
}
