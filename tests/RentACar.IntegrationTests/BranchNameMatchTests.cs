using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Güvenlik F5 — şube adı → şube çözümü TEK kural (<see cref="BranchNameMatch"/>): harf duyarsız aday; önce BİREBİR ad,
/// sonra AKTİF şube, sonra Kod. Toplu doldurma (<see cref="BranchBackfill"/>) ve oturum çözümü (middleware'in kullandığı
/// <see cref="BranchNameMatch.ResolveAsync"/>) aynı sonuca varır.
/// ORACLE (elle kurulmuş): "MERKEZ"(A1, aktif) + "Merkez"(B1, aktif) → "Merkez" yazan B1'e (Kod sırası A1'i seçerdi);
/// "Sahil"(A2, PASİF) + "sahil"(B2, aktif) → "SAHIL" yazan B2'ye (birebir yok; aktif tercih).
/// </summary>
[Collection("postgres")]
public sealed class BranchNameMatchTests(PostgresFixture fx)
{
    [Fact]
    public async Task Birebir_ad_once_sonra_aktif_sube_backfill_ve_oturum_cozumu_ayni()
    {
        var tenant = Guid.NewGuid();
        var owner = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(owner, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Code = $"bnm{tenant:N}"[..12], Name = "Şube adı kuralı" });
            await db.SaveChangesAsync();
        }

        var upperMerkez = new Branch { Kod = "A1", Ad = "MERKEZ" };
        var exactMerkez = new Branch { Kod = "B1", Ad = "Merkez" };
        var passiveSahil = new Branch { Kod = "A2", Ad = "Sahil", Aktif = false };
        var activeSahil = new Branch { Kod = "B2", Ad = "sahil" };
        using (var host = new TestHost(fx.AppConnectionString))
        using (var scope = host.ScopeFor(tenant))
        {
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            db.Branches.AddRange(upperMerkez, exactMerkez, passiveSahil, activeSahil);
            await db.SaveChangesAsync();
        }

        // Eski (FK'sız) kullanıcılar: owner bağlamı interceptor'sız → FK boş kalır.
        var merkezUser = new User { TenantId = tenant, UserName = "m1", DisplayName = "m1", Rol = UserRole.Operator, AtanmisSube = "Merkez", PasswordHash = "x" };
        var sahilUser = new User { TenantId = tenant, UserName = "s1", DisplayName = "s1", Rol = UserRole.Operator, AtanmisSube = "SAHIL", PasswordHash = "x" };
        await using (var db = new AppDbContext(owner, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Users.AddRange(merkezUser, sahilUser);
            await db.SaveChangesAsync();
        }

        // Oturum çözümü (middleware'in yolu).
        var sys = new SystemTenantContext { TenantId = tenant };
        await using (var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options, sys, sys))
        {
            await TenantGuc.OpenAsync(db, tenant);
            Assert.Equal(exactMerkez.Id, await BranchNameMatch.ResolveAsync(db, tenant, "Merkez"));
            Assert.Equal(activeSahil.Id, await BranchNameMatch.ResolveAsync(db, tenant, "SAHIL"));
            Assert.Null(await BranchNameMatch.ResolveAsync(db, tenant, "Olmayan"));
        }

        // Toplu doldurma aynı sonuca varır.
        await using (var db = new AppDbContext(owner, NullTenantContext.Instance, NullCurrentUser.Instance))
            await BranchBackfill.RunAsync(db);
        await using (var db = new AppDbContext(owner, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            Assert.Equal(exactMerkez.Id, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == merkezUser.Id)).AtanmisSubeId);
            Assert.Equal(activeSahil.Id, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == sahilUser.Id)).AtanmisSubeId);
        }
    }
}
