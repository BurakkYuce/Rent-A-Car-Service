using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class BranchScopeTests(PostgresFixture fx)
{
    private static async Task SeedVehiclesAsync(IServiceScope scope, params (string Plaka, string? Sube)[] vs)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (plaka, sube) in vs)
            db.Vehicles.Add(new Vehicle { Plaka = plaka, Sube = sube, Durum = VehicleStatus.Musait });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Operator_sees_only_own_branch_vehicles()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
            await SeedVehiclesAsync(seed,
                ("34MRK01", "Merkez"), ("34MRK02", "Merkez"), ("06ANK01", "Ankara"), ("34NUL01", null));

        // Operatör, Merkez şubesine atanmış → yalnız Merkez araçları.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        var vehicles = await op.ServiceProvider.GetRequiredService<VehicleService>().ListAsync();
        Assert.Equal(2, vehicles.Count);
        Assert.All(vehicles, v => Assert.Equal("Merkez", v.Sube));
    }

    [Fact]
    public async Task Admin_and_manager_see_all_branches()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
            await SeedVehiclesAsync(seed, ("34MRK01", "Merkez"), ("06ANK01", "Ankara"), ("34NUL01", null));

        // Admin (şube atansa bile) tüm şubeleri görür.
        using (var admin = host.ScopeFor(tenant, Guid.NewGuid(), "ad", UserRole.Admin, assignedBranch: "Merkez"))
            Assert.Equal(3, (await admin.ServiceProvider.GetRequiredService<VehicleService>().ListAsync()).Count);

        // Yönetici de tüm şubeler.
        using var mgr = host.ScopeFor(tenant, Guid.NewGuid(), "yo", UserRole.Yonetici, assignedBranch: "Ankara");
        Assert.Equal(3, (await mgr.ServiceProvider.GetRequiredService<VehicleService>().ListAsync()).Count);
    }

    [Fact]
    public async Task Operator_without_branch_sees_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
            await SeedVehiclesAsync(seed, ("34MRK01", "Merkez"), ("06ANK01", "Ankara"));

        // Şubesi atanmamış operatör → kapsam yok (tümü).
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: null);
        Assert.Equal(2, (await op.ServiceProvider.GetRequiredService<VehicleService>().ListAsync()).Count);
    }

    [Fact]
    public async Task Reservation_list_scoped_by_pickup_office()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
        {
            var factory = seed.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Reservations.Add(new Reservation { ReservationNo = "RZ-1", CikisOfisi = "Merkez", Durum = ReservationStatus.Rezerv });
            db.Reservations.Add(new Reservation { ReservationNo = "RZ-2", CikisOfisi = "Ankara", Durum = ReservationStatus.Rezerv });
            await db.SaveChangesAsync();
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        var list = await op.ServiceProvider.GetRequiredService<ReservationService>().ListAsync();
        Assert.Single(list);
        Assert.Equal("Merkez", list[0].CikisOfisi);
    }
}
