using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul B-0: seed <c>operator</c> (Merkez) kendi şubesinde kira açamıyordu — seed kullanıcıya yalnız şube ADINI
/// yazıyordu; FK doldurma (BranchBackfill) seed'den ve "Merkez" şubesinin oluşturulmasından ÖNCE koşuyordu.
/// ORACLE: beklenen kimlikler doğrudan DB'den ada göre okunur (seed/backfill kodundan türetilmez).
/// </summary>
[Collection("postgres")]
public sealed class SeedOperatorBranchTests
{
    [Fact]
    public async Task Ilk_kurulumda_seed_operatorun_sube_FK_si_ve_seed_ofisin_subesi_Merkez()
    {
        var pg = new PostgresFixture();
        await pg.InitializeAsync();
        try
        {
            await SeedAsync(pg);

            await using var db = OwnerDb(pg);
            var tenant = await db.Tenants.SingleAsync(t => t.Code == "yucerent");
            await TenantGuc.OpenAsync(db, tenant.Id);
            var merkez = await db.Branches.IgnoreQueryFilters()
                .SingleAsync(b => b.TenantId == tenant.Id && b.Ad == "Merkez");
            var op = await db.Users.AsNoTracking().SingleAsync(u => u.TenantId == tenant.Id && u.UserName == "operator");
            Assert.Equal("Merkez", op.AtanmisSube);
            Assert.Equal(merkez.Id, op.AtanmisSubeId);

            // Seed ofisi Merkez şubesine bağlı: operatör ilk kurulumdan itibaren kendi şubesinin ofisini seçebilir.
            var office = await db.Locations.IgnoreQueryFilters()
                .SingleAsync(l => l.TenantId == tenant.Id && l.Ad == "Merkez Ofis");
            Assert.Equal("Merkez", office.Sube);
            Assert.Equal(merkez.Id, office.SubeId);
        }
        finally { await pg.DisposeAsync(); }
    }

    [Fact]
    public async Task Sonraki_acilista_FK_siz_eski_kullanici_sube_adindan_doldurulur()
    {
        var pg = new PostgresFixture();
        await pg.InitializeAsync();
        try
        {
            await SeedAsync(pg);
            Guid tenantId, userId;
            await using (var db = OwnerDb(pg))
            {
                tenantId = (await db.Tenants.SingleAsync(t => t.Code == "yucerent")).Id;
                // Eski hesap: yalnız metin (owner bağlamı interceptor'sız → FK boş kalır).
                var legacy = new User
                {
                    TenantId = tenantId, UserName = "eski" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Eski",
                    Rol = UserRole.Operator, AtanmisSube = " merkez ", PasswordHash = "x", IsActive = true,
                };
                db.Users.Add(legacy);
                await db.SaveChangesAsync();
                userId = legacy.Id;
                Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).AtanmisSubeId);
            }

            await SeedAsync(pg); // ikinci açılış

            await using (var db = OwnerDb(pg))
            {
                await TenantGuc.OpenAsync(db, tenantId);
                var merkez = await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenantId && b.Ad == "Merkez");
                Assert.Equal(merkez.Id, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).AtanmisSubeId);
            }
        }
        finally { await pg.DisposeAsync(); }
    }

    private static async Task SeedAsync(PostgresFixture pg)
    {
        var setting = new Dictionary<string, string?> { [DbInitializer.SeedPasswordKey] = WebFixture.RandomPassword() };
        using var host = new TestHost(pg.AppConnectionString, s =>
        {
            s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(setting).Build());
            s.AddSingleton<IHostEnvironment>(new FakeEnvironment());
        });
        using var scope = host.ScopeFor(null);
        await DbInitializer.MigrateAndSeedAsync(scope.ServiceProvider, pg.OwnerConnectionString);
    }

    private static AppDbContext OwnerDb(PostgresFixture pg)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.OwnerConnectionString).Options,
               NullTenantContext.Instance, NullCurrentUser.Instance);

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "RentACar.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
