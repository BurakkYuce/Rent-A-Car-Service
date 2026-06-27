using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class VehicleSaleTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedVehicleAsync(IServiceScope scope, string plaka = "34SAT34")
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var v = new Vehicle { Plaka = plaka, Durum = VehicleStatus.Stokta };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    [Fact]
    public async Task Sale_posts_balanced_ledger_and_marks_vehicle_sold()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var vehicleId = await SeedVehicleAsync(scope);
        var alici = Guid.NewGuid();

        // net 100000 @0.20 → KDV 20000, brüt 120000.
        var id = await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = vehicleId, AliciCariId = alici, SatisNet = 100000m, KdvOrani = 0.20m, NoterNo = "N-1"
        });

        var sale = await sales.GetAsync(id);
        Assert.Equal("ST-000001", sale!.No);
        Assert.Equal(20000m, sale.KdvTutar);
        Assert.Equal(120000m, sale.GenelToplam);
        Assert.Equal(SatisDurum.Tamamlandi, sale.Durum);

        // Alıcı cari brüt kadar borçlanır (+120000).
        Assert.Equal(120000m, await cash.GetCariBalanceAsync(alici));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "AracSatis").ToListAsync();
        Assert.Equal(3, entries.Count); // Borç Cari + Alacak Gelir + Alacak KDV
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(120000m, debit);
        Assert.Equal(debit, credit); // dengeli
        Assert.Equal(100000m, entries.Single(e => e.AccountType == LedgerAccountType.Gelir).Amount.AmountInBase);
        Assert.Equal(20000m, entries.Single(e => e.AccountType == LedgerAccountType.Kdv).Amount.AmountInBase);

        var vehicle = await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == vehicleId);
        Assert.Equal(VehicleStatus.Satildi, vehicle.Durum);
    }

    [Fact]
    public async Task Double_sale_of_same_vehicle_is_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var vehicleId = await SeedVehicleAsync(scope);

        await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = vehicleId, AliciCariId = Guid.NewGuid(), SatisNet = 5000m, KdvOrani = 0.20m
        });
        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = vehicleId, AliciCariId = Guid.NewGuid(), SatisNet = 6000m, KdvOrani = 0.20m
        }));
    }

    [Fact]
    public async Task Validation_rejects_bad_input()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();

        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(new VehicleSaleInput
        { AliciCariId = Guid.NewGuid(), SatisNet = 100m })); // araç yok
        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(new VehicleSaleInput
        { VehicleId = Guid.NewGuid(), SatisNet = 100m })); // alıcı yok
        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(new VehicleSaleInput
        { VehicleId = Guid.NewGuid(), AliciCariId = Guid.NewGuid(), SatisNet = 0m })); // tutar yok
        // Var olmayan araç → bulunamadı.
        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(new VehicleSaleInput
        { VehicleId = Guid.NewGuid(), AliciCariId = Guid.NewGuid(), SatisNet = 100m }));
    }

    [Fact]
    public async Task Sale_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var vid = await SeedVehicleAsync(s1, "34T1T1");
            await s1.ServiceProvider.GetRequiredService<VehicleSaleService>()
                .CreateAsync(new VehicleSaleInput { VehicleId = vid, AliciCariId = Guid.NewGuid(), SatisNet = 1000m });
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<VehicleSaleService>().ListAsync());
    }

    [Fact]
    public async Task Sale_writes_audit_and_is_immutable()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "satici");
        var vehicleId = await SeedVehicleAsync(scope);
        await scope.ServiceProvider.GetRequiredService<VehicleSaleService>()
            .CreateAsync(new VehicleSaleInput { VehicleId = vehicleId, AliciCariId = Guid.NewGuid(), SatisNet = 2500m });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var audit = Assert.Single(await db.AuditLogs.Where(a => a.EntityName == "VehicleSales").ToListAsync());
        Assert.Equal("satici", audit.UserName);

        var sale = await db.VehicleSales.FirstAsync();
        sale.Aciklama = "tahrif";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync()); // immutable
    }
}
