using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Locations;
using RentACar.Application.Users;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Şube-FK tamamlama (roadmap F1, yapısal): BranchFkInterceptor yazımda Sube/AtanmisSube metnini
/// Branch FK'sine çözer (case-insensitive; eşleşmezse null; cross-tenant izole); BranchBackfill eski
/// (FK'siz) satırları doldurur. BAĞIMSIZ ORACLE: "Kadıköy" şube id'si, "  kadıköy  " metni onu çözer.
/// </summary>
[Collection("postgres")]
public sealed class BranchFkTests(PostgresFixture fx)
{
    private static Task<Guid> Branch(IServiceProvider sp, string kod, string ad)
        => sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = kod, Ad = ad });

    private static Task<Guid> Ofis(IServiceProvider sp, string kod, string? sube)
        => sp.GetRequiredService<LocationService>().CreateAsync(new LocationInput { Kod = kod, Ad = "Ofis " + kod, Sube = sube });

    [Fact]
    public async Task Interceptor_yazimda_sube_metnini_FKye_cozer_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var loc = sp.GetRequiredService<LocationService>();
        var branchId = await Branch(sp, "MRK", "Kadıköy");

        var id1 = await Ofis(sp, "OF1", "  kadıköy  ");     // farklı case + boşluk
        Assert.Equal(branchId, (await loc.GetAsync(id1))!.SubeId); // interceptor çözdü

        var id2 = await Ofis(sp, "OF2", "Olmayan Şube");    // eşleşmeyen
        Assert.Null((await loc.GetAsync(id2))!.SubeId);

        var id3 = await Ofis(sp, "OF3", null);              // boş
        Assert.Null((await loc.GetAsync(id3))!.SubeId);
    }

    [Fact]
    public async Task User_atanmis_sube_FKye_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var branchId = await Branch(sp, "ANK", "Ankara");

        var userId = await sp.GetRequiredService<UserService>().CreateAsync(new UserInput
        { UserName = "op1", Password = "gizli123", Rol = UserRole.Operator, AtanmisSube = "ankara" });

        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(branchId, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).AtanmisSubeId);
    }

    [Fact]
    public async Task Cross_tenant_sube_cozulmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var sA = host.ScopeFor(Guid.NewGuid());
        using var sB = host.ScopeFor(Guid.NewGuid());
        await Branch(sA.ServiceProvider, "MRK", "Kadıköy"); // yalnız tenant A

        var id = await Ofis(sB.ServiceProvider, "OF1", "Kadıköy"); // B'de aynı ad → B'nin şubesi yok
        Assert.Null((await sB.ServiceProvider.GetRequiredService<LocationService>().GetAsync(id))!.SubeId);
    }

    [Fact]
    public async Task Backfill_eski_FKsiz_satiri_doldurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var branchId = await Branch(sp, "MRK", "Kadıköy");
        var locId = await Ofis(sp, "OF1", "Kadıköy");

        // ESKİ satır simülasyonu: FK'yi raw SQL ile null'la (SaveChanges değil → interceptor atlanır).
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Locations\" SET \"SubeId\" = NULL WHERE \"Id\" = {locId}");
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Null((await db.Locations.AsNoTracking().SingleAsync(l => l.Id == locId)).SubeId);

        // Backfill tenant listesinden beslendiği için tenant kaydı şart.
        var ownerOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var seedDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            seedDb.Tenants.Add(new Tenant { Id = tenant, Code = $"fkt{tenant:N}"[..12], Name = "FK Backfill Test" });
            await seedDb.SaveChangesAsync();
        }
        await using (var ownerDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
            Assert.True(await BranchBackfill.RunAsync(ownerDb) >= 1);

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(branchId, (await db.Locations.AsNoTracking().SingleAsync(l => l.Id == locId)).SubeId);

        // İDEMPOTENT: ikinci koşu bu satırı yeniden doldurmaz (0 döner ya da bu satırı saymaz).
        await using (var ownerDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
            Assert.Equal(0, await BranchBackfill.RunAsync(ownerDb));
    }
}
