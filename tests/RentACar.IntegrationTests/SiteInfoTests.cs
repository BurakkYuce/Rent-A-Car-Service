using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Application.TenantSettings;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// V0 — halka açık site bilgileri ziyaretçiye (<c>role: null</c>) <see cref="FleetBranding.Facts"/> ile ulaşır ve başka
/// kiracıya sızmaz (<c>racar_app</c> + RLS). Google bağlantısı izin listesi saf fonksiyonda kilitli. Değerler elle yazıldı.
/// </summary>
[Collection("postgres")]
public sealed class SiteInfoTests(PostgresFixture fx)
{
    [Fact]
    public async Task Site_facts_reach_the_visitor_and_stay_inside_the_tenant()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var s = host.ScopeFor(tenant))
        {
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await svc.GetAsync();
            m.SiteDepozitoTutari = 7500m;
            m.SiteDepozitoAciklama = "  Nakit depozito olur.  ";
            m.SiteKmSinirsiz = true;
            m.SiteMinYas = 23;
            m.SiteMinEhliyetYili = 3;
            m.SiteSigortaOzeti = "Kasko dahil.";
            m.SiteIptalKosulu = "24 saat öncesine kadar ücretsiz.";
            m.SiteGooglePuani = 4.6m;
            m.SiteGoogleYorumSayisi = 87;
            m.SiteGoogleProfilUrl = "https://www.google.com/maps/place/ornek";
            m.SiteKurulusYili = 2015;
            await svc.SaveAsync(m);
        }

        using (var visitor = host.ScopeFor(tenant, role: null))
        {
            var b = await visitor.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetBrandingAsync();
            var f = b.Facts!;
            Assert.Equal(7500m, f.DepozitoTutari);
            Assert.Equal("Nakit depozito olur.", f.DepozitoAciklama);
            Assert.True(f.KmSinirsiz);
            Assert.Null(f.GunlukKmSiniri);
            Assert.Equal((23, 3), (f.MinYas, f.MinEhliyetYili));
            Assert.Equal("Kasko dahil.", f.SigortaOzeti);
            Assert.Equal("24 saat öncesine kadar ücretsiz.", f.IptalKosulu);
            Assert.Equal((4.6m, 87), (f.GooglePuani, f.GoogleYorumSayisi));
            Assert.Equal("https://www.google.com/maps/place/ornek", f.GoogleProfilUrl);
            Assert.NotNull(f.GooglePuanTarihi);
            Assert.Equal(2015, f.KurulusYili);
        }

        using (var other = host.ScopeFor(Guid.NewGuid(), role: null))
        {
            var f = (await other.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetBrandingAsync()).Facts!;
            Assert.Equal(new SiteFacts(), f);
        }
    }

    [Fact]
    public async Task Rating_without_link_is_rejected_by_the_service()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
        var m = await svc.GetAsync();
        m.SiteGooglePuani = 4.9m;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(m));
        Assert.Equal("siteGoogleProfilUrl", ex.Alan);
        Assert.Null((await svc.GetAsync()).SiteGooglePuani);
    }

    [Theory]
    [InlineData("https://maps.app.goo.gl/abc", true)]
    [InlineData("https://g.page/r/abc/review", true)]
    [InlineData("https://goo.gl/maps/abc", true)]
    [InlineData("https://www.google.com/maps/place/x", true)]
    [InlineData("https://google.com.tr/maps?cid=1", true)]
    [InlineData("https://maps.google.com/?cid=1", true)]
    [InlineData("HTTPS://WWW.GOOGLE.COM/maps", true)]
    [InlineData("http://www.google.com/maps", false)]
    [InlineData("https://www.google.com:8443/maps", false)]
    [InlineData("https://user@www.google.com/maps", false)]
    [InlineData("https://google.com.evil.example/maps", false)]
    [InlineData("https://notgoogle.com/maps", false)]
    [InlineData("https://g.page.evil.example/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("//www.google.com/maps", false)]
    [InlineData("", false)]
    public void Google_profile_link_allow_list(string url, bool allowed)
        => Assert.Equal(allowed, SiteInfoRules.IsGoogleProfileUrl(url));
}
