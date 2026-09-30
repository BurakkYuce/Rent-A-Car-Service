using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RentACar.Application.Fleet;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Infrastructure.Persistence.Repositories;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site logo ucu (<c>/marka/logo</c>) ve önbelleği — güvenlik incelemesi M1(c) + L1.
///
/// <para>BAĞIMSIZ ORACLE: beklenen sayılar senaryodan gelir ("16 eşzamanlı kaçırma → 1 okuma, 1 çözme";
/// "304 → 0 bayt okuması"). Sayaçlar üretim kodunu değil, onu SARAN test sarmalayıcılarını sayar.</para>
/// </summary>
[Collection("postgres")]
public sealed class WebLogoCacheTests(PostgresFixture fx)
{
    private static readonly Lazy<byte[]> Bomb = new(() => WebLogoTests.GrayPng(2000, 1_000_000));

    [Fact]
    public async Task Eszamanli_kacirmada_logo_tek_kez_okunur_ve_tek_kez_cozulur()
    {
        var resizeCalls = 0;
        var loadCalls = 0;
        using var cache = new WebLogoCache(resize: b =>
        {
            Interlocked.Increment(ref resizeCalls);
            return Application.Common.ImageProcessing.TryCreateWebLogo(b);
        });
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logo = new PublicLogo(WebLogoTests.GrayPng(1200, 300), "v1");
        async Task<PublicLogo?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loadCalls);
            await gate.Task; // tüm istekler uçuşa katılsın diye okuma "yavaş"
            return logo;
        }

        var tenant = Guid.NewGuid();
        var requests = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => cache.GetOrCreateAsync(tenant, "v1", Load))).ToArray();
        await Task.Delay(200);
        gate.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, loadCalls);
        Assert.Equal(1, resizeCalls);
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal("image/png", results[0]!.ContentType);

        // Sonraki istek önbellekten: ne okuma ne çözme.
        await cache.GetOrCreateAsync(tenant, "v1", Load);
        Assert.Equal(1, loadCalls);
        Assert.Equal(1, resizeCalls);
    }

    [Fact]
    public async Task Butce_disi_logo_cozulmez_servis_edilmez_ve_negatif_onbellege_yazilir()
    {
        var resizeCalls = 0;
        var loadCalls = 0;
        using var cache = new WebLogoCache(resize: _ => { Interlocked.Increment(ref resizeCalls); return null; });
        Task<PublicLogo?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loadCalls);
            return Task.FromResult<PublicLogo?>(new PublicLogo(Bomb.Value, "v1"));
        }

        var tenant = Guid.NewGuid();
        Assert.Null(await cache.GetOrCreateAsync(tenant, "v1", Load));
        Assert.Null(await cache.GetOrCreateAsync(tenant, "v1", Load));
        Assert.Equal(0, resizeCalls); // çözücüye HİÇ gitmedi
        Assert.Equal(1, loadCalls);   // 1 MB'lık bayt her istekte yeniden okunmuyor
    }

    /// <summary>L1: 304 ve önbellek isabeti LogoBytes'ı DB'den çekmez. Bayt okuması
    /// (<see cref="IPublicBrandingRepository.GetLogoAsync"/>) yalnız ilk kaçırmada.</summary>
    [Fact]
    public async Task Logo_ucu_304_ve_onbellek_isabetinde_baytlari_okumaz()
    {
        var tenant = await SeedTenantAsync();
        using (var host = new TestHost(fx.AppConnectionString))
        using (var admin = host.ScopeFor(tenant))
            await admin.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SetLogoAsync(WebLogoTests.GrayPng(1200, 300));

        var counter = new CallCounter();
        using var factory = new PublicSiteFactory(fx, tenant, configureServices: s =>
        {
            s.RemoveAll(typeof(IPublicBrandingRepository));
            s.AddScoped<IPublicBrandingRepository>(sp => new CountingBrandingRepository(
                ActivatorUtilities.CreateInstance<PublicBrandingRepository>(sp), counter));
        });
        var client = factory.Client();

        using var first = await client.GetAsync("/marka/logo");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.Equal(1, counter.LogoReads);

        using var second = await client.GetAsync("/marka/logo"); // önbellek isabeti
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, counter.LogoReads);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/marka/logo");
        conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.Tag));
        using var notModified = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(1, counter.LogoReads); // 304 bayt okumadı
    }

    private async Task<Guid> SeedTenantAsync()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "logo" + Guid.NewGuid().ToString("N")[..10], Name = "Logo", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private sealed class CallCounter
    {
        private int _logoReads;
        public int LogoReads => Volatile.Read(ref _logoReads);
        public void Hit() => Interlocked.Increment(ref _logoReads);
    }

    private sealed class CountingBrandingRepository(IPublicBrandingRepository inner, CallCounter counter)
        : IPublicBrandingRepository
    {
        public Task<FleetBranding> GetAsync(Guid tenantId, CancellationToken ct = default)
            => inner.GetAsync(tenantId, ct);

        public Task<PublicLogo?> GetLogoAsync(Guid tenantId, CancellationToken ct = default)
        {
            counter.Hit();
            return inner.GetLogoAsync(tenantId, ct);
        }

        public Task<string?> GetCanonicalHostAsync(Guid tenantId, CancellationToken ct = default)
            => inner.GetCanonicalHostAsync(tenantId, ct);
    }
}
