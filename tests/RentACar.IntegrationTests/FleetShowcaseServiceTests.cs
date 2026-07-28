using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-4: public-site filo vitrini. BAĞIMSIZ ORACLE — beklenen değerler elle kurulmuş senaryodan
/// türetilir. Ziyaretçi bağlamı `Role: null` ile taklit edilir (PublicTenantContext'in gerçek
/// şekli — BranchScope.EffectiveFilter bunu Unrestricted'e çözer, PR-3'te doğrulanmış ilke).
/// </summary>
[Collection("postgres")]
public sealed class FleetShowcaseServiceTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    private static async Task<Guid> CreateGroupAsync(TestHost host, Guid tenantId, string ad, bool aktif = true, int? webSira = null)
    {
        using var scope = host.ScopeFor(tenantId);
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var id = await groups.CreateAsync(new VehicleGroupInput { Kod = ad.ToUpperInvariant(), Ad = ad, WebSira = webSira, Aktif = aktif });
        return id;
    }

    private static async Task<Guid> CreateVehicleAsync(TestHost host, Guid tenantId, string grup, bool webRezKapat = false)
    {
        using var scope = host.ScopeFor(tenantId);
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        return await vehicles.CreateAsync(new VehicleInput
        {
            Plaka = "34FS" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Durum = VehicleStatus.Musait,
            Grup = grup,
            WebRezKapat = webRezKapat,
        });
    }

    private static async Task AddPhotoAsync(TestHost host, Guid tenantId, Guid vehicleId)
    {
        using var scope = host.ScopeFor(tenantId);
        var photos = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        await photos.AddAsync(vehicleId, TinyPng);
    }

    [Fact]
    public async Task WebRezKapat_tek_araclik_grup_vitrine_girmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await CreateGroupAsync(host, tenantId, "Ekonomik", webSira: 0);
        await CreateVehicleAsync(host, tenantId, "Ekonomik", webRezKapat: true);

        using var scope = host.ScopeFor(tenantId, role: null);
        var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
        var cards = await svc.ListShowcaseGroupsAsync();

        Assert.Empty(cards);
    }

    [Fact]
    public async Task Pasif_grup_vitrine_girmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await CreateGroupAsync(host, tenantId, "Lüks", aktif: false, webSira: 0);
        await CreateVehicleAsync(host, tenantId, "Lüks");

        using var scope = host.ScopeFor(tenantId, role: null);
        var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
        var cards = await svc.ListShowcaseGroupsAsync();

        Assert.Empty(cards);
    }

    [Fact]
    public async Task Kapak_min_sira_fotografidir_fotografsiz_temsilci_kart_yine_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        await CreateGroupAsync(host, tenantId, "Fotosuz", webSira: 0);
        await CreateVehicleAsync(host, tenantId, "Fotosuz"); // hiç foto yok

        await CreateGroupAsync(host, tenantId, "Fotolu", webSira: 1);
        var vehicleWithPhotos = await CreateVehicleAsync(host, tenantId, "Fotolu");
        await AddPhotoAsync(host, tenantId, vehicleWithPhotos); // Sira 0
        await AddPhotoAsync(host, tenantId, vehicleWithPhotos); // Sira 1

        using var scope = host.ScopeFor(tenantId, role: null);
        var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
        var photos = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var cards = await svc.ListShowcaseGroupsAsync();

        Assert.Equal(2, cards.Count);
        var fotosuz = cards.Single(c => c.Ad == "Fotosuz");
        Assert.Null(fotosuz.CoverPhotoId); // kart yine listede — foto olmadan

        var fotolu = cards.Single(c => c.Ad == "Fotolu");
        var meta = await photos.ListMetaAsync(vehicleWithPhotos);
        Assert.Equal(meta[0].Id, fotolu.CoverPhotoId); // Sira 0'daki foto
    }

    [Fact]
    public async Task GetGroupDetail_gruptaki_tum_uygun_araclarin_fotograflarini_birlestirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var groupId = await CreateGroupAsync(host, tenantId, "SUV", webSira: 0);

        var v1 = await CreateVehicleAsync(host, tenantId, "SUV");
        var v2 = await CreateVehicleAsync(host, tenantId, "SUV");
        await CreateVehicleAsync(host, tenantId, "SUV", webRezKapat: true); // dışlanır — fotoğrafı da yok zaten

        await AddPhotoAsync(host, tenantId, v1);
        await AddPhotoAsync(host, tenantId, v2);

        using var scope = host.ScopeFor(tenantId, role: null);
        var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
        var detail = await svc.GetGroupDetailAsync(groupId);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.PhotoIds.Count); // v1 + v2, tek araca ait değil
    }

    [Fact]
    public async Task Bilinmeyen_veya_pasif_groupId_null_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var pasifGroupId = await CreateGroupAsync(host, tenantId, "Pasif", aktif: false);

        using var scope = host.ScopeFor(tenantId, role: null);
        var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();

        Assert.Null(await svc.GetGroupDetailAsync(Guid.NewGuid()));
        Assert.Null(await svc.GetGroupDetailAsync(pasifGroupId));
    }

    [Fact]
    public async Task Bos_filo_bos_liste_doner_istisna_atmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        using var scope = host.ScopeFor(tenantId, role: null);
        var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
        var cards = await svc.ListShowcaseGroupsAsync();

        Assert.Empty(cards);
    }

    [Fact]
    public async Task GetPublicAsync_capraz_tenant_photoId_null_doner_RLS_tek_basina_izole_eder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var vehicleA = await CreateVehicleAsync(host, tenantA, "X");
        await AddPhotoAsync(host, tenantA, vehicleA);

        Guid photoId;
        using (var scopeA = host.ScopeFor(tenantA, role: null))
        {
            var photosA = scopeA.ServiceProvider.GetRequiredService<VehiclePhotoService>();
            photoId = (await photosA.ListMetaAsync(vehicleA))[0].Id;
        }

        // A kendi fotoğrafını görebilir.
        using (var scopeA2 = host.ScopeFor(tenantA, role: null))
        {
            var photosA2 = scopeA2.ServiceProvider.GetRequiredService<VehiclePhotoService>();
            Assert.NotNull(await photosA2.GetPublicAsync(photoId));
        }

        // B, A'nın photoId'sini tahmin etse bile RLS satırı hiç döndürmez — vehicleId kontrolü YOK, tek sınır RLS.
        using var scopeB = host.ScopeFor(tenantB, role: null);
        var photosB = scopeB.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        Assert.Null(await photosB.GetPublicAsync(photoId));
        Assert.Null(await photosB.GetPublicThumbAsync(photoId));
    }

    [Fact]
    public async Task GetBrandingAsync_FirmaMarka_bosken_Tenant_Name_e_duser()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var owner = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            owner.Tenants.Add(new Tenant { Id = tenantId, Code = "fs" + Guid.NewGuid().ToString("N")[..8], Name = "Acme Rent", IsActive = true });
            await owner.SaveChangesAsync();
        }

        using var host = new TestHost(fx.AppConnectionString);

        // FirmaMarka boş — Tenant.Name'e düşer.
        using (var scope = host.ScopeFor(tenantId, role: null))
        {
            var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
            var branding = await svc.GetBrandingAsync();
            Assert.Equal("Acme Rent", branding.Marka);
        }

        // FirmaMarka doldurulunca kendisi kullanılır.
        using (var scope = host.ScopeFor(tenantId))
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.TenantSettings.Add(new TenantSettings { TenantId = tenantId, FirmaMarka = "AcmeBrand" });
            await db.SaveChangesAsync();
        }

        using (var scope = host.ScopeFor(tenantId, role: null))
        {
            var svc = scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
            var branding = await svc.GetBrandingAsync();
            Assert.Equal("AcmeBrand", branding.Marka);
        }
    }
}
