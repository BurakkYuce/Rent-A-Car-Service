using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-3.5: cache decorator — saf birim testi (DB yok). BAĞIMSIZ ORACLE: sayaçlı sahte
/// `IPublicTenantResolver` ile inner çağrı SAYISI doğrulanır (gerçek DB'ye gitmeden).
/// </summary>
public sealed class CachedPublicTenantResolverTests
{
    private sealed class CountingFakeResolver : IPublicTenantResolver
    {
        public readonly Dictionary<string, int> CallsPerHost = new();
        public PublicTenantResult Result { get; set; } = new(PublicTenantResolution.Found, Guid.NewGuid());

        public Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default)
        {
            CallsPerHost[host] = CallsPerHost.GetValueOrDefault(host) + 1;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task Ayni_host_art_arda_cagrilinca_inner_bir_kez_calisir()
    {
        var fake = new CountingFakeResolver();
        using var cached = new CachedPublicTenantResolver(fake);

        await cached.ResolveAsync("yucerent.rentpro.com");
        await cached.ResolveAsync("yucerent.rentpro.com");
        await cached.ResolveAsync("yucerent.rentpro.com");

        Assert.Equal(1, fake.CallsPerHost["yucerent.rentpro.com"]);
    }

    [Fact]
    public async Task Farkli_host_ayri_inner_cagrisi_tetikler()
    {
        var fake = new CountingFakeResolver();
        using var cached = new CachedPublicTenantResolver(fake);

        await cached.ResolveAsync("a.rentpro.com");
        await cached.ResolveAsync("b.rentpro.com");
        await cached.ResolveAsync("a.rentpro.com");

        Assert.Equal(1, fake.CallsPerHost["a.rentpro.com"]);
        Assert.Equal(1, fake.CallsPerHost["b.rentpro.com"]);
    }

    [Fact]
    public async Task Negatif_sonuc_da_cachelenir()
    {
        var fake = new CountingFakeResolver { Result = new PublicTenantResult(PublicTenantResolution.NotFound) };
        using var cached = new CachedPublicTenantResolver(fake);

        var r1 = await cached.ResolveAsync("bilinmeyen.rentpro.com");
        var r2 = await cached.ResolveAsync("bilinmeyen.rentpro.com");

        Assert.Equal(PublicTenantResolution.NotFound, r1.Kind);
        Assert.Equal(PublicTenantResolution.NotFound, r2.Kind);
        Assert.Equal(1, fake.CallsPerHost["bilinmeyen.rentpro.com"]); // negatif sonuç da cache'lendi
    }

    [Fact]
    public async Task Sonuc_dogru_donuyor_host_kucuk_harfe_normalize_edilir()
    {
        var tenantId = Guid.NewGuid();
        var fake = new CountingFakeResolver { Result = new PublicTenantResult(PublicTenantResolution.Found, tenantId) };
        using var cached = new CachedPublicTenantResolver(fake);

        var lower = await cached.ResolveAsync("yucerent.rentpro.com");
        var upper = await cached.ResolveAsync("YUCERENT.rentpro.com"); // aynı host, farklı case

        Assert.Equal(tenantId, lower.TenantId);
        Assert.Equal(tenantId, upper.TenantId);
        Assert.Equal(1, fake.CallsPerHost.Values.Sum()); // tek anahtara normalize edildi → tek inner çağrı
    }
}
