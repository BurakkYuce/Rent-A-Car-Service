using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Availability;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class AvailabilityTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset WinFrom = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WinTo = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    private static async Task SeedAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var v1 = new Vehicle { Plaka = "34A001", Grup = "A", Sube = "Merkez", Durum = VehicleStatus.Musait };
        var v2 = new Vehicle { Plaka = "34A002", Grup = "A", Durum = VehicleStatus.Musait };
        var v3 = new Vehicle { Plaka = "34A003", Grup = "A", Durum = VehicleStatus.Musait };
        var v4 = new Vehicle { Plaka = "34A004", Grup = "A", Durum = VehicleStatus.Musait };
        var v5 = new Vehicle { Plaka = "34A005", Grup = "A", Durum = VehicleStatus.Pasif };
        var v7 = new Vehicle { Plaka = "34B007", Grup = "B", Durum = VehicleStatus.Musait };
        db.Vehicles.AddRange(v1, v2, v3, v4, v5, v7);

        // V2: çakışan AKTİF kira [12,20] → hariç.
        db.Rentals.Add(new RentalContract
        { SozlesmeNo = "K-2", VehicleId = v2.Id, Durum = RentalStatus.Kirada,
          BasTar = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero), BitTar = new(2026, 6, 20, 0, 0, 0, TimeSpan.Zero) });
        // V4: çakışmayan kira [1,9] (pencereden önce biter) → MÜSAİT.
        db.Rentals.Add(new RentalContract
        { SozlesmeNo = "K-4", VehicleId = v4.Id, Durum = RentalStatus.Kirada,
          BasTar = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), BitTar = new(2026, 6, 9, 0, 0, 0, TimeSpan.Zero) });
        // V3: çakışan açık rezervasyon [8,11] → hariç.
        db.Reservations.Add(new Reservation
        { ReservationNo = "R-3", VehicleId = v3.Id, Durum = ReservationStatus.Rezerv,
          BasTar = new(2026, 6, 8, 0, 0, 0, TimeSpan.Zero), BitTar = new(2026, 6, 11, 0, 0, 0, TimeSpan.Zero) });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Finds_only_free_vehicles_excluding_overlaps_and_inactive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        await SeedAsync(scope);

        var available = await scope.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(WinFrom, WinTo);

        // V1 (boş) + V4 (kira pencereden önce biter) + V7 (boş, farklı grup). V2/V3 çakışır, V5 pasif.
        var plakalar = available.Select(v => v.Plaka).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { "34A001", "34A004", "34B007" }, plakalar);
    }

    [Fact]
    public async Task Group_filter_narrows_results()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        await SeedAsync(scope);

        var grupA = await scope.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(WinFrom, WinTo, grup: "A");
        Assert.Equal(new[] { "34A001", "34A004" }, grupA.Select(v => v.Plaka).OrderBy(p => p).ToArray());
    }

    [Fact]
    public async Task Operator_branch_scope_applies_to_availability()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
            await SeedAsync(seed);

        // Operatör Merkez → yalnız Merkez araçları (V1). Seçtiği şube override edemez.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        var available = await op.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(WinFrom, WinTo, sube: "Ankara");
        Assert.Equal(new[] { "34A001" }, available.Select(v => v.Plaka).ToArray());
    }

    [Fact]
    public async Task Invalid_date_range_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.FindAvailableAsync(WinTo, WinFrom));
        await Assert.ThrowsAsync<ValidationException>(() => svc.FindAvailableAsync(WinFrom, WinFrom));
    }

    [Fact]
    public async Task Availability_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await SeedAsync(s1);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var available = await s2.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(WinFrom, WinTo);
        Assert.Empty(available);
    }
}
