using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Application.TenantSettings;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site marka kimliği: vurgu rengi (<c>SiteVurguRengi</c>) + logo, ziyaretçi bağlamında
/// (<c>role: null</c> — PublicTenantContext'in gerçek şekli) okunur.
///
/// <para>BAĞIMSIZ ORACLE: renkler ve logo baytları testte elle yazılır, aynen geri okunur. İzolasyon
/// <c>racar_app</c> (RLS) bağlantısıyla ölçülür — TestHost uygulama bağlantısını kullanır.</para>
/// </summary>
[Collection("postgres")]
public sealed class SiteBrandIdentityTests(PostgresFixture fx)
{
    /// <summary>Başlığı geçerli PNG baytı (logo kuralları yalnız imza + IHDR okur).</summary>
    private static byte[] Png(int width, int height)
    {
        var b = new byte[200];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }.CopyTo(b, 0);
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        b[199] = 0x5A; // içerik ayırt edilsin diye son bayt
        return b;
    }

    private static async Task SaveAsync(IServiceProvider sp, Action<TenantSettingsModel> change)
    {
        var svc = sp.GetRequiredService<TenantSettingsService>();
        var m = await svc.GetAsync();
        change(m);
        await svc.SaveAsync(m);
    }

    [Fact]
    public async Task Vurgu_rengi_kaydedilir_normalize_edilir_ve_temizlenebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<TenantSettingsService>();

        Assert.Null((await svc.GetAsync()).SiteVurguRengi); // seçmeyen firmada NULL (varsayılan renk)

        await SaveAsync(sp, m => m.SiteVurguRengi = " #0B5D6B ");
        Assert.Equal("#0b5d6b", (await svc.GetAsync()).SiteVurguRengi);

        foreach (var bad in new[] { "petrol", "#0b5d6", "#0b5d6bb", "0b5d6b", "#0b5d6b;}" })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync(sp, m => m.SiteVurguRengi = bad));
            Assert.StartsWith("Site vurgu rengi", ex.Message);
        }
        Assert.Equal("#0b5d6b", (await svc.GetAsync()).SiteVurguRengi); // hiçbir biçimsiz değer yazılmadı

        await SaveAsync(sp, m => m.SiteVurguRengi = null);
        Assert.Null((await svc.GetAsync()).SiteVurguRengi);
    }

    /// <summary>Servis palet anahtarının yalnız BİÇİMİNİ doğrular (<c>^[a-z0-9-]{1,30}$</c>, kırpılır, küçük harfe
    /// çevrilir); temaya aitlik uçta. Başka temanın anahtarı serviste kabul edilir — site onu varsayılana düşürür.</summary>
    [Fact]
    public async Task Palet_anahtari_bicimle_dogrulanir_normalize_edilir_ve_ziyaretciye_okunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var s = host.ScopeFor(tenant))
        {
            var sp = s.ServiceProvider;
            var svc = sp.GetRequiredService<TenantSettingsService>();
            Assert.Null((await svc.GetAsync()).SitePaleti);

            await SaveAsync(sp, m => m.SitePaleti = " Gece-Mavisi ");
            Assert.Equal("gece-mavisi", (await svc.GetAsync()).SitePaleti);

            foreach (var bad in new[] { "gece mavisi", "gece_mavisi", "gök", "petrol;}", "a</style>", new string('a', 31) })
            {
                var ex = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync(sp, m => m.SitePaleti = bad));
                Assert.StartsWith("Site paleti", ex.Message);
            }
            Assert.Equal("gece-mavisi", (await svc.GetAsync()).SitePaleti);

            await SaveAsync(sp, m => { m.SiteTemasi = "kontuar"; m.SitePaleti = "bordo"; }); // aitlik serviste değil
            Assert.Equal("bordo", (await svc.GetAsync()).SitePaleti);
        }

        using (var visitor = host.ScopeFor(tenant, role: null))
            Assert.Equal("bordo", (await visitor.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetBrandingAsync()).Palette);
        using (var otherVisitor = host.ScopeFor(Guid.NewGuid(), role: null))
            Assert.Null((await otherVisitor.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetBrandingAsync()).Palette);

        using (var s = host.ScopeFor(tenant))
        {
            await SaveAsync(s.ServiceProvider, m => m.SitePaleti = "  ");
            Assert.Null((await s.ServiceProvider.GetRequiredService<TenantSettingsService>().GetAsync()).SitePaleti);
        }
    }

    [Fact]
    public async Task Ziyaretci_bagliminda_renk_ve_logo_yalniz_kendi_firmasindan_okunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var withBrand = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var logo = Png(640, 160);

        using (var admin = host.ScopeFor(withBrand))
        {
            await SaveAsync(admin.ServiceProvider, m => { m.FirmaMarka = "Kıyı Oto"; m.SiteVurguRengi = "#c0392b"; });
            await admin.ServiceProvider.GetRequiredService<TenantSettingsService>().SetLogoAsync(logo);
        }
        using (var admin = host.ScopeFor(plain))
            await SaveAsync(admin.ServiceProvider, m => m.FirmaMarka = "Düz Oto");

        using (var visitor = host.ScopeFor(withBrand, role: null))
        {
            var showcase = visitor.ServiceProvider.GetRequiredService<FleetShowcaseService>();
            var b = await showcase.GetBrandingAsync();
            Assert.Equal("Kıyı Oto", b.BrandName);
            Assert.Equal("#c0392b", b.AccentColor);
            Assert.False(string.IsNullOrEmpty(b.LogoVersion));

            var served = await showcase.GetLogoAsync();
            Assert.NotNull(served);
            Assert.Equal(logo, served!.Bytes);
            Assert.Equal(b.LogoVersion, served.Version); // sayfadaki adres ile uç aynı sürümü görür
        }

        using (var visitor = host.ScopeFor(plain, role: null))
        {
            var showcase = visitor.ServiceProvider.GetRequiredService<FleetShowcaseService>();
            var b = await showcase.GetBrandingAsync();
            Assert.Equal("Düz Oto", b.BrandName);
            Assert.Null(b.AccentColor);
            Assert.Null(b.LogoVersion);                    // logo yok → site marka adını basar
            Assert.Null(await showcase.GetLogoAsync());  // öbür firmanın logosu SIZMAZ
        }
    }

    [Fact]
    public async Task Logo_degisince_surum_degisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
            await admin.ServiceProvider.GetRequiredService<TenantSettingsService>().SetLogoAsync(Png(640, 160));

        string first;
        using (var visitor = host.ScopeFor(tenant, role: null))
            first = (await visitor.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetBrandingAsync()).LogoVersion!;

        await Task.Delay(20); // damga mikro-saniye çözünürlüklü; iki yazım aynı tick'e düşmesin
        using (var admin = host.ScopeFor(tenant))
            await admin.ServiceProvider.GetRequiredService<TenantSettingsService>().SetLogoAsync(Png(800, 200));

        using (var visitor = host.ScopeFor(tenant, role: null))
        {
            var second = (await visitor.ServiceProvider.GetRequiredService<FleetShowcaseService>().GetBrandingAsync()).LogoVersion;
            Assert.NotNull(first);
            Assert.NotEqual(first, second); // eski logonun uzun önbellekte kalmasını önleyen kural
        }
    }
}
