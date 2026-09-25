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
    // now-göreli gelecek (rezervasyon geçmişe kapalı — TarihPolitikasi); span 4 gün korunur.
    private static readonly DateTimeOffset Bas = DateTimeOffset.UtcNow.AddDays(3);
    private static readonly DateTimeOffset Bit = Bas.AddDays(4);

    private static BookingInput Input(Guid vehicle, Guid customer, DateTimeOffset? bas = null, DateTimeOffset? bit = null)
        => new() { MusteriId = customer, VehicleId = vehicle, BasTar = bas ?? Bas, BitTar = bit ?? Bit, GunlukUcret = 100m };

    [Fact]
    public async Task Reservation_create_gets_gapless_number_and_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();

        // Servis müşteri/araç varlığını doğruluyor → gerçek kayıtlar.
        var sp = scope.ServiceProvider;
        var id1 = await svc.CreateAsync(Input(await TestArac.YeniAsync(sp), await TestCari.YeniAsync(sp)));
        var id2 = await svc.CreateAsync(Input(await TestArac.YeniAsync(sp), await TestCari.YeniAsync(sp)));

        var r1 = await svc.GetAsync(id1);
        var r2 = await svc.GetAsync(id2);
        BelgeNoOracle.BeklenenlerdenBiri(2, 1, r1!.ReservationNo);
        BelgeNoOracle.BeklenenlerdenBiri(2, 2, r2!.ReservationNo);
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

        var sp = scope.ServiceProvider;
        var id = await svc.CreateAsync(Input(await TestArac.YeniAsync(sp), await TestCari.YeniAsync(sp)));
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

        var resId = await resSvc.CreateAsync(Input(
            await TestArac.YeniAsync(scope.ServiceProvider), await TestCari.YeniAsync(scope.ServiceProvider)));
        var rentalId = await resSvc.ConvertToRentalAsync(resId);

        var res = await resSvc.GetAsync(resId);
        Assert.Equal(ReservationStatus.KirayaCevrildi, res!.Durum);
        Assert.Equal(rentalId, res.RentalContractId);

        var rental = await rentSvc.GetAsync(rentalId);
        Assert.NotNull(rental);
        Assert.Equal(RentalStatus.Kirada, rental!.Durum);
        BelgeNoOracle.BeklenenlerdenBiri(1, 1, rental.SozlesmeNo);
        Assert.Equal(resId, rental.ReservationId);
        Assert.Equal(400m, rental.Bakiye);
    }

    [Fact]
    public async Task Overlapping_rental_rejected_sequentially()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
        var vehicle = await TestArac.YeniAsync(scope.ServiceProvider);
        var cari = await TestCari.YeniAsync(scope.ServiceProvider);

        await svc.CreateDirectAsync(Input(vehicle, cari));
        // Çakışan aralık → reddedilir
        await Assert.ThrowsAsync<AvailabilityConflictException>(
            () => svc.CreateDirectAsync(Input(vehicle, cari, Bas.AddDays(1), Bit.AddDays(1))));
        // Çakışmayan aralık (bitişik, [bit, bit+2)) → kabul
        var id = await svc.CreateDirectAsync(Input(vehicle, cari, Bit, Bit.AddDays(2)));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Concurrent_double_booking_exactly_one_wins_and_numbering_is_gapless()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var vehicle = await TestArac.YeniAsync(host, tenant);
        var cari = await TestCari.YeniAsync(host, tenant);

        async Task<bool> Book()
        {
            using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "racer");
            var svc = scope.ServiceProvider.GetRequiredService<RentalService>();
            try
            {
                await svc.CreateDirectAsync(Input(vehicle, cari));
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
        BelgeNoOracle.BeklenenlerdenBiri(1, 1, single.SozlesmeNo);
    }

    [Fact]
    public async Task Bookings_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<RentalService>()
                .CreateDirectAsync(Input(await TestArac.YeniAsync(s1.ServiceProvider), await TestCari.YeniAsync(s1.ServiceProvider)));

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
        await svc.CreateDirectAsync(Input(await TestArac.YeniAsync(scope.ServiceProvider), await TestCari.YeniAsync(scope.ServiceProvider)));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var log = Assert.Single(await db.AuditLogs.Where(a => a.EntityName == "Rentals").ToListAsync());
        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal("auditor", log.UserName);
        Assert.Contains(BelgeNoOracle.Bekle(1, 1), log.NewValues);   // audit log numarayı taşımalı
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
