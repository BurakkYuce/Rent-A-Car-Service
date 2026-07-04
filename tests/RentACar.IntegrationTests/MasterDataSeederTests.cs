using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Master varsayılan seed'i (combobox'ların "seç" kısmı dolu başlasın). BAĞIMSIZ ORACLE: elle sayılan liste
/// uzunlukları (Marka=20, Renk=12, Segment=8), doğru Türkçe imlâ korunur ("Gümüş"/"Hız Cezası" — ASCII'ye
/// bozulmaz; Kod ASCII-fold: Gümüş→GUMUS), İDEMPOTENT (ikinci koşu 0 ekler), dolu kategoriye DOKUNMAZ.
/// SeedTenantAsync GUC'u tid'e sabitler → sonraki sorgular owner+RLS altında o tenant'ı görür.
/// </summary>
[Collection("postgres")]
public sealed class MasterDataSeederTests(PostgresFixture fx)
{
    private AppDbContext OwnerDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);

    private async Task<Guid> NewTenantAsync()
    {
        await using var db = OwnerDb();
        var t = new Tenant { Code = "seed" + Guid.NewGuid().ToString("N")[..8], Name = "Seed Test" };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static Task<int> CountAsync<T>(AppDbContext db, Guid tid) where T : class, RentACar.Domain.Common.ITenantOwned
        => db.Set<T>().IgnoreQueryFilters().CountAsync(x => x.TenantId == tid);

    [Fact]
    public async Task Seed_masterlari_doldurur_ve_turkce_korur()
    {
        var tid = await NewTenantAsync();
        await using var db = OwnerDb();
        var n = await MasterDataSeeder.SeedTenantAsync(db, tid); // GUC=tid + seed
        db.ChangeTracker.Clear();

        Assert.True(n > 0);
        Assert.Equal(20, await CountAsync<Brand>(db, tid));         // elle sayılan liste uzunlukları
        Assert.Equal(12, await CountAsync<VehicleColor>(db, tid));
        Assert.Equal(8, await CountAsync<VehicleSegment>(db, tid));
        // Türkçe imlâ Ad'da korunur; Kod ASCII-fold
        Assert.True(await db.Set<VehicleColor>().IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tid && x.Ad == "Gümüş" && x.Kod == "GUMUS"));
        Assert.True(await db.Set<PenaltyType>().IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tid && x.Ad == "Hız Cezası"));
    }

    [Fact]
    public async Task Seed_idempotent_ikinci_kosuda_artmaz()
    {
        var tid = await NewTenantAsync();
        await using (var db1 = OwnerDb()) await MasterDataSeeder.SeedTenantAsync(db1, tid);

        await using var db = OwnerDb();
        var n2 = await MasterDataSeeder.SeedTenantAsync(db, tid); // ikinci koşu
        db.ChangeTracker.Clear();
        Assert.Equal(0, n2);                                       // tüm kategoriler dolu → 0 eklendi
        Assert.Equal(20, await CountAsync<Brand>(db, tid));        // artmadı
    }

    [Fact]
    public async Task Seed_dolu_kategoriye_dokunmaz()
    {
        var tid = await NewTenantAsync();
        await using var db = OwnerDb();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {tid.ToString()}, false)");
        db.Set<Brand>().Add(new Brand { TenantId = tid, Kod = "OZEL", Ad = "Özel Marka" }); // kullanıcı markası
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await MasterDataSeeder.SeedTenantAsync(db, tid); // Brand dolu → atlanır; boş kategoriler dolar
        db.ChangeTracker.Clear();

        Assert.Equal(1, await CountAsync<Brand>(db, tid));         // yalnız kullanıcının markası (varsayılan 20 EKLENMEDİ)
        Assert.Equal(12, await CountAsync<VehicleColor>(db, tid)); // Renk boştu → dolduruldu
    }
}
