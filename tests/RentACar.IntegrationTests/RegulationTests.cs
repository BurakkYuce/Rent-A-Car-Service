using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Regulation;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class RegulationTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Vade_panosu_aggregates_and_buckets_all_sources()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var reg = scope.ServiceProvider.GetRequiredService<RegulationService>();
        var vade = scope.ServiceProvider.GetRequiredService<VadeService>();
        var arac = Guid.NewGuid();

        await reg.AddInsuranceAsync(arac, InsuranceType.Trafik, Now.AddYears(-1), Now.AddDays(5), 1000m, "P1", "Allianz", null); // ≤7
        await reg.AddMtvAsync(arac, "2026-2", 800m, Now.AddDays(20));   // ≤30
        await reg.AddInspectionAsync(arac, Now.AddYears(-2).AddDays(-1), Now.AddDays(-3), 600m); // geçmiş

        var items = await vade.GetAllAsync(Now);
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i.Tur == "Trafik" && i.Bucket == VadeBucket.YediGun);
        Assert.Contains(items, i => i.Tur == "MTV" && i.Bucket == VadeBucket.OtuzGun);
        Assert.Contains(items, i => i.Tur == "Muayene" && i.Bucket == VadeBucket.Gecmis);

        var warnings = await vade.GetWarningsAsync(Now);
        Assert.Equal(3, warnings.Count); // hiçbiri İleri değil
    }

    [Fact]
    public async Task Paid_mtv_excluded_from_vade()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var reg = scope.ServiceProvider.GetRequiredService<RegulationService>();
        var vade = scope.ServiceProvider.GetRequiredService<VadeService>();
        var arac = Guid.NewGuid();

        // Ödenmiş MTV doğrudan repo ile (servis Odendi set etmiyor) — DB'ye ödenmiş ekleyelim:
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MtvRecords.Add(new() { VehicleId = arac, Donem = "2025-1", Tutar = 500m, Vade = Now.AddDays(3), Odendi = true });
            await db.SaveChangesAsync();
        }

        var items = await vade.GetAllAsync(Now);
        Assert.Empty(items); // ödenmiş MTV vade panosuna girmez
    }

    [Fact]
    public async Task Regulation_is_tenant_isolated_and_audited()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1, Guid.NewGuid(), "auditor"))
            await s1.ServiceProvider.GetRequiredService<RegulationService>()
                .AddInsuranceAsync(Guid.NewGuid(), InsuranceType.Kasko, Now, Now.AddYears(1), 5000m, "K1", "Axa", "Acente");

        // İzolasyon
        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<RegulationService>().ListInsuranceAsync());

        // Audit (T1)
        using var s1b = host.ScopeFor(t1);
        var factory = s1b.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.NotEmpty(await db.AuditLogs.Where(a => a.EntityName == "InsurancePolicies").ToListAsync());
    }
}
