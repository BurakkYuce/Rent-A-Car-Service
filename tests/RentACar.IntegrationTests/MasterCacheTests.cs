using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// Master/referans cache (ITenantCache) — VehicleService üzerinden: cache HIT DB'yi atlar (doğrudan eklenen
/// görünmez), yazımda INVALIDATE tazeler, TENANT izolasyon (A'nın cache'i B'de görünmez).
/// </summary>
[Collection("postgres")]
public sealed class MasterCacheTests(PostgresFixture fx)
{
    [Fact]
    public async Task Cache_hit_ve_yazimda_invalidate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await svc.CreateAsync(new VehicleInput { Plaka = "34 CA 1" });
        Assert.Single(await svc.ListAsync()); // ilk çağrı → cache'ler

        // Cache'i ATLAYARAK doğrudan ekle (VehicleService dışı → invalidate YOK)
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Vehicles.Add(new Vehicle { Plaka = "34 CA 2", Durum = VehicleStatus.Musait });
            await db.SaveChangesAsync();
        }
        Assert.Single(await svc.ListAsync()); // HÂLÂ 1 → cache çalışıyor (doğrudan eklenen görünmedi)

        // VehicleService ile ekle → invalidate → taze liste
        await svc.CreateAsync(new VehicleInput { Plaka = "34 CA 3" });
        Assert.Equal(3, (await svc.ListAsync()).Count); // invalidate sonrası: 2 servis + 1 doğrudan
    }

    [Fact]
    public async Task Tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (var sa = host.ScopeFor(a))
        {
            await sa.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 A 1" });
            Assert.Single(await sa.ServiceProvider.GetRequiredService<VehicleService>().ListAsync()); // A cache'ler
        }
        using (var sb = host.ScopeFor(b))
        {
            // B, A'nın cache'ini GÖRMEZ (anahtar tenant-kapsamlı) → B'nin kendi (boş) listesi
            Assert.Empty(await sb.ServiceProvider.GetRequiredService<VehicleService>().ListAsync());
        }
    }
}
