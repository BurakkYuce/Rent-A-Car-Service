using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site yeniden tasarımı — GERÇEK PublicSite boru hattı (PublicSiteFactory) üzerinden kabuk
/// davranışları: 404 gövdesi (SEO H-2), firma vurgu renginin sayfaya basılması, veri yoksa bölüm yok (T2),
/// satır içi betik yok (CSP script-src 'self').
///
/// <para>BAĞIMSIZ ORACLE: marka adı, renk ve beklenen metinler testte elle yazılır. Beklenen kontrast
/// yazı rengi (#1d2227) sarı (#ffd400) için elle hesaplanmıştır: beyaz 1.4:1, mürekkep 11:1.</para>
/// </summary>
[Collection("postgres")]
public sealed class PublicSiteDesignTests(PostgresFixture fx)
{
    private const string Brand = "Kiyi Oto Deneme";

    private async Task<Guid> SeedTenantAsync()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "tsr" + Guid.NewGuid().ToString("N")[..10], Name = Brand, IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    /// <summary>H-2: eşleşmeyen adres, yayında olmayan ilan/yazı/sayfa ve doğrudan /not-found — hepsi 404
    /// KALIR ama gövde kabukla (marka + menü) ve çıkış bağlantılarıyla gelir; güvenlik başlıkları korunur.
    /// Eskiden gövde 0 bayttı.</summary>
    [Fact]
    public async Task Bulunamayan_adresler_404_ve_kabuklu_govde_doner()
    {
        var tenant = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        foreach (var path in new[] { "/araclar/yok-boyle-ilan", "/a/b/c", "/blog/yok-boyle-yazi", "/yok-boyle-sayfa", "/not-found" })
        {
            using var r = await client.GetAsync(path);
            var html = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.NotFound, $"{path} → {(int)r.StatusCode}");
            Assert.Contains("burada değil", WebUtility.HtmlDecode(html));
            Assert.Contains(Brand, html);                        // kabuk markayla basıldı (async hazırlık beklendi)
            Assert.Contains("class=\"bar\"", html);
            Assert.Contains("href=\"/musaitlik\"", html);
            Assert.True(r.Headers.Contains("Content-Security-Policy"), $"{path}: CSP başlığı yok");
        }
    }

    [Fact]
    public async Task Firma_vurgu_rengi_kontrast_guvenceli_degiskenlerle_basilir()
    {
        var tenant = await SeedTenantAsync();
        using (var host = new TestHost(fx.AppConnectionString))
        using (var s = host.ScopeFor(tenant))
        {
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await svc.GetAsync();
            m.SiteVurguRengi = "#ffd400";
            await svc.SaveAsync(m);
        }

        using var factory = new PublicSiteFactory(fx, tenant);
        var html = await factory.Client().GetStringAsync("/");

        Assert.Contains("--brand:#ffd400", html);       // dolgu: firmanın rengi aynen
        Assert.Contains("--on-brand:#1d2227", html);    // sarı üstünde koyu yazı
        Assert.DoesNotContain("--brand-text:#ffd400;", html[..html.IndexOf("@media", StringComparison.Ordinal)]); // açık temada metin tonu koyulaştırıldı
    }

    /// <summary>T2: yayında ilan yoksa tarife tahtası ve filo bölümü BASILMAZ; arama formu ve süreç kalır.
    /// CSP: sayfadaki her &lt;script&gt; ya harici dosya ya da JSON-LD veri bloğu.</summary>
    [Fact]
    public async Task Ilan_yokken_bos_bolum_basilmaz_ve_satir_ici_betik_yok()
    {
        var tenant = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, tenant);
        var html = WebUtility.HtmlDecode(await factory.Client().GetStringAsync("/"));

        Assert.Contains("name=\"bas\"", html);
        Assert.Contains("Üç adımda araç kiralayın", html);
        Assert.DoesNotContain("Günlük fiyatlar", html);
        Assert.DoesNotContain("id=\"filo\"", html);
        Assert.DoesNotContain("Örnek hesap", html);

        foreach (Match tag in Regex.Matches(html, "<script[^>]*>"))
            Assert.True(tag.Value.Contains("src=", StringComparison.Ordinal)
                        || tag.Value.Contains("application/ld+json", StringComparison.Ordinal),
                $"satır içi betik: {tag.Value}");
    }
}
