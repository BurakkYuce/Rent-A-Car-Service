using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class DeliveryReturnTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    private static BookingInput Input(Guid vehicle) => new()
    {
        MusteriId = Guid.NewGuid(), VehicleId = vehicle, BasTar = Bas, BitTar = Bit,
        GunlukUcret = 100m, KmLimit = 400, FazlaKmUcret = 2m, YakitBirimUcret = 50m
    };

    [Fact]
    public async Task Deliver_then_return_computes_charges_and_completes()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await svc.CreateDirectAsync(Input(Guid.NewGuid()));
        Assert.True(await svc.DeliverAsync(id, cikisKm: 1000, cikisYakit: 8));

        // 1 gün geç + 100 fazla km + 2 eksik yakıt
        Assert.True(await svc.ReturnAsync(id, donusKm: 1500, donusYakit: 6, gercekDonus: Bit.AddDays(1)));

        var c = await svc.GetAsync(id);
        Assert.Equal(RentalStatus.Tamamlandi, c!.Durum);
        Assert.Equal(100, c.FazlaKm);
        Assert.Equal(200m, c.FazlaKmBedeli);
        Assert.Equal(2, c.EksikYakit);
        Assert.Equal(100m, c.YakitBedeli);
        Assert.Equal(1, c.UzatmaGun);
        Assert.Equal(100m, c.UzatmaBedeli);
        Assert.Equal(800m, c.GenelToplam); // 400 + 200 + 100 + 100
        Assert.Equal(800m, c.Bakiye);      // tahsilat 0
    }

    [Fact]
    public async Task Cannot_return_before_delivery()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await svc.CreateDirectAsync(Input(Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.ReturnAsync(id, 1100, 8, Bit));
    }

    [Fact]
    public async Task Return_km_less_than_delivery_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await svc.CreateDirectAsync(Input(Guid.NewGuid()));
        await svc.DeliverAsync(id, 1000, 8);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.ReturnAsync(id, 900, 8, Bit)); // 900 < 1000
    }

    [Fact]
    public async Task Vehicle_available_again_after_return()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
        var vehicle = Guid.NewGuid();

        var id1 = await svc.CreateDirectAsync(Input(vehicle));
        // Aynı araç/aralık ikinci kira → çakışma
        await Assert.ThrowsAsync<AvailabilityConflictException>(() => svc.CreateDirectAsync(Input(vehicle)));

        // İlkini teslim + dönüş (Tamamlandı → exclusion WHERE Durum=0 kapsamı dışına çıkar)
        await svc.DeliverAsync(id1, 1000, 8);
        await svc.ReturnAsync(id1, 1200, 8, Bit);

        // Artık aynı araç/aralık tekrar kiralanabilir
        var id2 = await svc.CreateDirectAsync(Input(vehicle));
        Assert.NotEqual(Guid.Empty, id2);
    }

    [Fact]
    public async Task Deliver_writes_audit_update()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "auditor");
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await svc.CreateDirectAsync(Input(Guid.NewGuid()));
        await svc.DeliverAsync(id, 1000, 8);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var updates = await db.AuditLogs
            .Where(a => a.EntityName == "Rentals" && a.Action == AuditAction.Update)
            .ToListAsync();
        Assert.NotEmpty(updates);
        Assert.Contains(updates, u => u.NewValues != null && u.NewValues.Contains("CikisKm"));
    }
}
