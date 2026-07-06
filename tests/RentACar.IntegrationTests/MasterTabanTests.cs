using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.CancelReasons;
using RentACar.Application.FuelKinds;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// O12d sapma kapanışı kanıtı: Kod+Ad(+Aktif) master ailesi generic tabana (MasterTanimService)
/// indikten sonra ITenantCache aile üyelerinin HEPSİNDE çalışır — daha önce yalnız Brand'de vardı.
/// Örnek üyeler (FuelKind, CancelReason) üzerinden MasterCacheTests deseni: cache HIT DB'yi atlar
/// (doğrudan eklenen görünmez), yazımda INVALIDATE tazeler, TENANT izolasyon (A'nın cache'i B'de görünmez).
/// </summary>
[Collection("postgres")]
public sealed class MasterTabanTests(PostgresFixture fx)
{
    [Fact]
    public async Task FuelKind_cache_hit_ve_yazimda_invalidate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<FuelKindService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await svc.CreateAsync(new FuelKindInput { Kod = "BNZ", Ad = "Benzin" });
        Assert.Single(await svc.ListAsync()); // ilk çağrı → cache'ler

        // Cache'i ATLAYARAK doğrudan ekle (servis dışı → invalidate YOK)
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.FuelKinds.Add(new FuelKind { Kod = "DZL", Ad = "Dizel" });
            await db.SaveChangesAsync();
        }
        Assert.Single(await svc.ListAsync()); // HÂLÂ 1 → cache HIT (doğrudan eklenen görünmedi)

        // Servisle ekle → invalidate → taze liste
        await svc.CreateAsync(new FuelKindInput { Kod = "LPG", Ad = "LPG" });
        Assert.Equal(3, (await svc.ListAsync()).Count); // 2 servis + 1 doğrudan

        // Yazım (update) da invalidate eder: pasifleştirilen üye aktif listeden düşer
        var list = await svc.ListAsync();
        var bnz = list.Single(x => x.Kod == "BNZ");
        await svc.UpdateAsync(bnz.Id, new FuelKindInput { Kod = "BNZ", Ad = "Benzin", Aktif = false });
        Assert.Equal(2, (await svc.ListActiveAsync()).Count); // DZL + LPG (BNZ pasif)
    }

    [Fact]
    public async Task CancelReason_cache_hit_ve_yazimda_invalidate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CancelReasonService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await svc.CreateAsync(new CancelReasonInput { Kod = "MUS", Ad = "Müşteri vazgeçti" });
        Assert.Single(await svc.ListAsync()); // ilk çağrı → cache'ler

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CancelReasons.Add(new CancelReason { Kod = "ARC", Ad = "Araç arızası" });
            await db.SaveChangesAsync();
        }
        Assert.Single(await svc.ListAsync()); // HÂLÂ 1 → cache HIT

        await svc.CreateAsync(new CancelReasonInput { Kod = "FIY", Ad = "Fiyat itirazı" });
        Assert.Equal(3, (await svc.ListAsync()).Count); // invalidate sonrası: 2 servis + 1 doğrudan
    }

    [Fact]
    public async Task FuelKind_cache_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (var sa = host.ScopeFor(a))
        {
            var svc = sa.ServiceProvider.GetRequiredService<FuelKindService>();
            await svc.CreateAsync(new FuelKindInput { Kod = "BNZ", Ad = "Benzin" });
            Assert.Single(await svc.ListAsync()); // A cache'ler
        }
        using (var sb = host.ScopeFor(b))
        {
            // B, A'nın cache'ini GÖRMEZ (anahtar tenant-kapsamlı) → B'nin kendi (boş) listesi
            Assert.Empty(await sb.ServiceProvider.GetRequiredService<FuelKindService>().ListAsync());
        }
    }
}
