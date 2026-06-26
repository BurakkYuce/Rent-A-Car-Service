using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class BookingTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2026, 7, 5, 10, 0, 0, TimeSpan.Zero);

    private static BookingInput Input(Guid vehicle, Guid customer, DateTimeOffset? bas = null, DateTimeOffset? bit = null)
        => new() { MusteriId = customer, VehicleId = vehicle, BasTar = bas ?? Bas, BitTar = bit ?? Bit, GunlukUcret = 100m };

    [Fact]
    public async Task Reservation_create_gets_gapless_number_and_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();

        var id1 = await svc.CreateAsync(Input(Guid.NewGuid(), Guid.NewGuid()));
        var id2 = await svc.CreateAsync(Input(Guid.NewGuid(), Guid.NewGuid()));

        var r1 = await svc.GetAsync(id1);
        var r2 = await svc.GetAsync(id2);
        Assert.Equal("RZ-000001", r1!.ReservationNo);
        Assert.Equal("RZ-000002", r2!.ReservationNo);
        Assert.Equal(ReservationStatus.Rezerv, r1.Durum);
        Assert.Equal(4, r1.Gun);          // 4 gün
        Assert.Equal(400m, r1.Tutar);     // 4 * 100
    }

    [Fact]
    public async Task Reservation_confirm_and_invalid_transition()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();

        var id = await svc.CreateAsync(Input(Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(await svc.ConfirmAsync(id));
        Assert.Equal(ReservationStatus.Onayli, (await svc.GetAsync(id))!.Durum);

        // Onaylı'yı tekrar Confirm → geçersiz geçiş
        await Assert.ThrowsAsync<ValidationException>(() => svc.ConfirmAsync(id));
    }

    [Fact]
    public async Task Convert_reservation_to_rental_tasfiye()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var resSvc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var rentSvc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var resId = await resSvc.CreateAsync(Input(Guid.NewGuid(), Guid.NewGuid()));
        var rentalId = await resSvc.ConvertToRentalAsync(resId);

        var res = await resSvc.GetAsync(resId);
        Assert.Equal(ReservationStatus.KirayaCevrildi, res!.Durum);
        Assert.Equal(rentalId, res.RentalContractId);

        var rental = await rentSvc.GetAsync(rentalId);
        Assert.NotNull(rental);
        Assert.Equal(RentalStatus.Kirada, rental!.Durum);
        Assert.Equal("KS-000001", rental.SozlesmeNo);
        Assert.Equal(resId, rental.ReservationId);
        Assert.Equal(400m, rental.Bakiye);
    }

    [Fact]
    public async Task Overlapping_rental_rejected_sequentially()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
        var vehicle = Guid.NewGuid();

        await svc.CreateDirectAsync(Input(vehicle, Guid.NewGuid()));
        // Çakışan aralık → reddedilir
        await Assert.ThrowsAsync<AvailabilityConflictException>(
            () => svc.CreateDirectAsync(Input(vehicle, Guid.NewGuid(), Bas.AddDays(1), Bit.AddDays(1))));
        // Çakışmayan aralık (bitişik, [bit, bit+2)) → kabul
        var id = await svc.CreateDirectAsync(Input(vehicle, Guid.NewGuid(), Bit, Bit.AddDays(2)));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Concurrent_double_booking_exactly_one_wins_and_numbering_is_gapless()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var vehicle = Guid.NewGuid();

        async Task<bool> Book()
        {
            using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "racer");
            var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
            try
            {
                await svc.CreateDirectAsync(Input(vehicle, Guid.NewGuid()));
                return true;
            }
            catch (AvailabilityConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Book(), Book(), Book(), Book(), Book());
        Assert.Equal(1, results.Count(ok => ok)); // tam olarak BİR kazanan (exclusion constraint)

        // Gap-less: kaybedenlerin sıra tahsisi rollback olur → kazanan KS-000001
        using var scope = host.ScopeFor(tenant);
        var rentSvc = scope.ServiceProvider.GetRequiredService<RentalService>();
        var rentals = await rentSvc.ListAsync();
        var single = Assert.Single(rentals);
        Assert.Equal("KS-000001", single.SozlesmeNo);
    }

    [Fact]
    public async Task Bookings_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<RentalService>()
                .CreateDirectAsync(Input(Guid.NewGuid(), Guid.NewGuid()));

        using var s2 = host.ScopeFor(t2);
        var factory = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rawCount = await db.Database
            .SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"Rentals\"")
            .SingleAsync();
        Assert.Equal(0, rawCount); // T2 T1'in kirasını göremez (RLS)
    }

    [Fact]
    public async Task Rental_create_writes_audit()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "auditor");
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
        await svc.CreateDirectAsync(Input(Guid.NewGuid(), Guid.NewGuid()));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(await db.AuditLogs.Where(a => a.EntityName == "Rentals").ToListAsync());
        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal("auditor", log.UserName);
        Assert.Contains("KS-000001", log.NewValues);
    }

    [Fact]
    public async Task Invalid_dates_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateDirectAsync(Input(Guid.NewGuid(), Guid.NewGuid(), Bit, Bas))); // bit < bas
    }
}
