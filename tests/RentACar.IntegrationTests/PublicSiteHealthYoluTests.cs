using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık sitede tenant çözümlemesinden MUAF yollar. DB gerektirmez — middleware doğrudan
/// çağrılır (PublicTenantResolverTests.cs deseni: HTTP yığını kurulmaz).
///
/// <para><b>Neden bu test var (canlı denemede bulundu):</b> muafiyet <c>StartsWith("/health")</c>
/// ile ÖNEK üzerinden yapılıyordu, oysa uygulamada yalnız <c>/health/live</c> maplenmiş. Sonuç:
/// <c>/health</c> isteği tenant çözümlemesini atlıyor, hiçbir uca denk gelmediği için Razor'a
/// düşüyor, sayfa tenant-kapsamlı DB'ye gidiyor ve <i>"Tenant bağlamı boş"</i> ile <b>500</b>
/// patlıyordu — temiz bir 404 yerine. Muafiyet artık TAM EŞLEŞME.</para>
///
/// <para>Aynı testin ikinci işi bir REGRESYONU önlemek: muafiyeti tamamen kaldırmak da yanlış
/// olurdu — konteyner/orkestratör sağlık probu siteye IP ya da bilinmeyen bir Host ile gelir;
/// <c>/health/live</c> o durumda da 200 dönmek ZORUNDA.</para>
/// </summary>
public sealed class PublicSiteHealthYoluTests
{
    /// <summary>Her çağrıda "bu host bilinmiyor" diyen çözümleyici + kaç kez sorulduğunun sayacı.</summary>
    private sealed class UnknownHostResolver : IPublicTenantResolver
    {
        public int Calls { get; private set; }
        public Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PublicTenantResult(PublicTenantResolution.NotFound));
        }
    }

    private static async Task<(int Status, bool SonrakiCalisti, int CozucuCagri)> RequestAsync(string path)
    {
        var resolver = new UnknownHostResolver();
        var mw = new TenantHostResolutionMiddleware(resolver);

        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddScoped<PublicTenantContext>().BuildServiceProvider()
        };
        ctx.Request.Host = new HostString("bilinmeyen.example.com");
        ctx.Request.Path = path;

        var next = false;
        await mw.InvokeAsync(ctx, _ => { next = true; return Task.CompletedTask; });
        return (ctx.Response.StatusCode, next, resolver.Calls);
    }

    [Fact]
    public async Task Maplenmis_saglik_ucu_bilinmeyen_hostta_bile_GECER()
    {
        var (status, next, call) = await RequestAsync("/health/live");

        Assert.True(next);          // pipeline devam etti (uç 200 dönebilsin)
        Assert.Equal(200, status);     // 404'e çevrilmedi
        Assert.Equal(0, call);        // tenant çözümlemesi HİÇ çalışmadı
    }

    /// <summary>
    /// MAPLENMEMİŞ bir sağlık yolu artık muaf DEĞİL: tenant çözümlemesine girer, bilinmeyen host
    /// olduğu için temiz 404 döner. Eski önek-muafiyetinde burası Razor'a düşüp 500 veriyordu.
    /// </summary>
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/healthz")]
    public async Task Maplenmemis_saglik_yolu_MUAF_DEGIL_temiz_404_doner(string path)
    {
        var (status, next, call) = await RequestAsync(path);

        Assert.False(next);         // Razor'a DÜŞMEDİ (500'ün kaynağı buydu)
        Assert.Equal(404, status);
        Assert.Equal(1, call);
    }

    /// <summary>Platform-seviyesi uçlar (Caddy ask, statik dosya) muafiyetini KORUR — daraltma
    /// yalnız sağlık yoluna aitti.</summary>
    [Theory]
    [InlineData("/dogrulama/ask")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/css/site.css")]
    public async Task Platform_ve_statik_yollar_muaf_KALIR(string path)
    {
        var (_, next, call) = await RequestAsync(path);

        Assert.True(next);
        Assert.Equal(0, call);
    }

    /// <summary>Tenant'a bağlı DİNAMİK uzantılı uçlar muaf OLMAMALI (PR-9 dersi korunuyor).</summary>
    [Theory]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    public async Task Tenant_bagimli_dinamik_uclar_cozumlemeye_GIRER(string path)
    {
        var (status, next, call) = await RequestAsync(path);

        Assert.False(next);
        Assert.Equal(404, status);
        Assert.Equal(1, call);
    }
}
