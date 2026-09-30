using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class PublicSiteSeoTests
{
    private static string? RobotsHeader(HttpResponseMessage r)
        => r.Headers.TryGetValues("X-Robots-Tag", out var v) ? string.Join(",", v) : null;

    /// <summary>M-3: işlem sayfaları "noindex", parametreli müsaitlik "noindex, follow";
    /// parametresiz müsaitlik ve içerik sayfaları başlık TAŞIMAZ.</summary>
    [Fact]
    public async Task Noindex_basligi_yalniz_islem_sayfalarinda_ve_parametreli_aramada()
    {
        var (t, _) = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, t);
        var client = factory.Client();

        var cases = new (string Path, string? Expected)[]
        {
            ("/talep-alindi", "noindex"),
            ("/cok-istek", "noindex"),
            ("/rezervasyon-talebi", "noindex"),
            ("/rezervasyon-talebi?ilan=abc", "noindex"),
            ("/musaitlik?ilan=abc&sube=merkez", "noindex, follow"),
            ("/musaitlik", null),
            ("/", null),
            ("/iletisim", null),
        };
        foreach (var (path, expected) in cases)
        {
            using var r = await client.GetAsync(path);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{path} → {(int)r.StatusCode}");
            Assert.True(expected == RobotsHeader(r), $"{path}: beklenen '{expected}', gelen '{RobotsHeader(r)}'");
        }
    }

    /// <summary>
    /// M-4: arama limiti (1/dk) aşılınca 302 DEĞİL 429 + Retry-After; gövde boş değil (bekleme sayfası
    /// 429 koduyla render edilir), yönlendirme yok, noindex.
    /// </summary>
    [Fact]
    public async Task Arama_limiti_asilinca_429_ve_Retry_After_doner_yonlendirme_yok()
    {
        var (t, _) = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, t, searchPermit: 1);
        var client = factory.Client();

        using var first = await client.GetAsync("/musaitlik");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.GetAsync("/musaitlik?ilan=abc");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Null(second.Headers.Location);
        var retry = second.Headers.RetryAfter?.Delta;
        Assert.NotNull(retry);
        Assert.InRange(retry!.Value.TotalSeconds, 1, 60); // pencere 60 sn
        Assert.Equal("noindex", RobotsHeader(second));
        Assert.Equal("text/html", second.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html", await second.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>M-4: talep POST'u da (form gönderimi) limit aşımında 429 alır; gövde GET olarak render edilir.</summary>
    [Fact]
    public async Task Talep_limiti_asilinca_POST_429_doner()
    {
        var (t, _) = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, t, bookingPermit: 1);
        var client = factory.Client();
        var form = () => new FormUrlEncodedContent([new KeyValuePair<string, string>("adSoyad", "x")]);

        using var first = await client.PostAsync("/rezervasyon-talebi/gonder", form());
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode); // izin tüketildi (antiforgery reddi olabilir)

        using var second = await client.PostAsync("/rezervasyon-talebi/gonder", form());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter?.Delta);
        Assert.Contains("<html", await second.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>M-5: HEAD istekleri 200 döner (eskiden 404) ve gövde taşımaz.</summary>
    [Fact]
    public async Task HEAD_istegi_200_doner_govdesiz()
    {
        var (t, _) = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, t);
        var client = factory.Client();

        foreach (var path in new[] { "/", "/robots.txt", "/sitemap.xml", "/iletisim" })
        {
            using var r = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"HEAD {path} → {(int)r.StatusCode}");
            Assert.Empty(await r.Content.ReadAsByteArrayAsync());
        }
    }

    /// <summary>L-5: çözülemeyen eski GUID ilan adresi 410 (302 → / DEĞİL) ve gövde boş değil;
    /// çözülen GUID hâlâ slug'a 301.</summary>
    [Fact]
    public async Task Cozulemeyen_eski_GUID_ilan_adresi_410_cozulen_301()
    {
        var (t, _) = await SeedTenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        var (listingId, slug) = await SeedListingAsync(host, t);
        using var factory = new PublicSiteFactory(fx, t);
        var client = factory.Client();

        using var gone = await client.GetAsync($"/araclar/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        Assert.Null(gone.Headers.Location);
        Assert.Contains("<html", await gone.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var moved = await client.GetAsync($"/araclar/{listingId}");
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal($"/araclar/{slug}", moved.Headers.Location?.OriginalString);
    }
}
