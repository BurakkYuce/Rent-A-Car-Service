using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Model guard — çok-kiracılık güvenlik kapsamı. AppDbContext'in inline config'leri
/// Configurations/ altına bölündü ve HasQueryFilter MERKEZİ jenerik döngüye taşındı
/// (OnModelCreating). Bu test, o döngünün kapsamını modelden bağımsız oracle ile doğrular:
/// ITenantOwned olan HER entity'de tenant query filter'ı VAR olmalı (biri bile atlanırsa
/// sessiz çapraz-tenant sızıntı riski), ITenantOwned OLMAYAN platform entity'lerinde
/// (Tenant / TenantDomain / User / KurKaydi) filter OLMAMALI.
/// </summary>
[Collection("postgres")]
public sealed class ModelGuardTests(PostgresFixture fx)
{
    private AppDbContext CreateContext()
    {
        // Fixture'ın KISITLI app bağlantısı (racar_app) + scheduler deseniyle aynı
        // SystemTenantContext — üretimdeki gibi tenant'lı bir context örneği kurar.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fx.AppConnectionString)
            .Options;
        var identity = new SystemTenantContext { TenantId = Guid.NewGuid() };
        return new AppDbContext(options, identity, identity);
    }

    [Fact]
    public void Her_ITenantOwned_entity_tenant_query_filter_tasir()
    {
        using var db = CreateContext();

        var eksikler = db.Model.GetEntityTypes()
            .Where(et => et.BaseType is null
                         && typeof(ITenantOwned).IsAssignableFrom(et.ClrType)
                         && et.GetDeclaredQueryFilters().Count == 0)
            .Select(et => et.ClrType.Name)
            .ToList();

        Assert.True(eksikler.Count == 0,
            "Tenant filter'ı OLMAYAN ITenantOwned entity'ler (çapraz-tenant sızıntı riski): "
            + string.Join(", ", eksikler));
    }

    [Fact]
    public void Platform_entityleri_filtresiz_ve_kapsam_disinda_kimse_yok()
    {
        using var db = CreateContext();

        // Filter taşımayan entity'ler TAM OLARAK platform tabloları olmalı — ne eksik ne fazla.
        var filtresizler = db.Model.GetEntityTypes()
            .Where(et => et.BaseType is null && et.GetDeclaredQueryFilters().Count == 0)
            .Select(et => et.ClrType)
            .OrderBy(t => t.Name)
            .ToList();

        // PR-B: PlatformBelge/PlatformBelgeHedef eklendi — platformdan tenant'lara PDF dağıtımı.
        // PR-C: PaylasimLink eklendi — anonim sözleşme linki token'ı RLS'li tablodan ÇÖZÜLEMEZ
        //       (GUC set edilmemiş istekte sorgu sessizce 0 satır döner), bu yüzden platform tablosu.
        //       PDF'in kendisi SozlesmePdf'te ve O tenant-owned + RLS'li (kişisel veri orada).
        // Bu liste bilinçli olarak ELLE tutuluyor: yeni bir filtresiz (=RLS'siz) tablo eklemek,
        // izolasyonun uygulama katmanına taşınması demek ve bu testi güncellemeyi ZORUNLU kılıyor.
        // 2026-08-17: KullaniciIzinIstisna eklendi — istisnalar LOGIN sırasında (tenant GUC'u
        //       henüz yokken) okunup claim'e yazılır; merkezi filtre o okumayı boş döndürürdü.
        //       Users ile aynı komut-bazlı RLS deseni (SELECT GUC-boşken açık, yazma tenant-kilitli).
        Type[] beklenenPlatform =
        [
            typeof(KullaniciIzinIstisna), typeof(KurKaydi), typeof(PaylasimLink),
            typeof(PlatformBelge), typeof(PlatformBelgeHedef),
            typeof(Tenant), typeof(TenantDomain), typeof(User),
        ];
        Assert.Equal(beklenenPlatform, filtresizler);

        // Platform entity'leri ITenantOwned DEĞİL (merkezi döngü onlara dokunmaz).
        Assert.All(filtresizler, t => Assert.False(typeof(ITenantOwned).IsAssignableFrom(t)));
    }

    [Fact]
    public void Filter_kapsami_ITenantOwned_sayisiyla_birebir()
    {
        using var db = CreateContext();

        var entityTypes = db.Model.GetEntityTypes().Where(et => et.BaseType is null).ToList();
        var tenantOwned = entityTypes.Count(et => typeof(ITenantOwned).IsAssignableFrom(et.ClrType));
        var filtreli = entityTypes.Count(et => et.GetDeclaredQueryFilters().Count > 0);

        // Bölme öncesi durum: 68 inline HasQueryFilter == 68 ITenantOwned entity (bağımsız sayım).
        // Merkezi döngü aynı kapsamı üretmeli; yeni entity eklendikçe iki sayı birlikte artar.
        Assert.Equal(tenantOwned, filtreli);
        Assert.True(tenantOwned >= 68, $"ITenantOwned entity sayısı geriledi: {tenantOwned} < 68");
    }
}
