using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class InvoiceTests(PostgresFixture fx)
{
    private static BookingInput Rental(Guid cari, Guid vehicle) => new()
    {
        MusteriId = cari, VehicleId = vehicle,
        BasTar = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero),
        BitTar = new DateTimeOffset(2026, 12, 5, 9, 0, 0, TimeSpan.Zero),
        GunlukUcret = 100m // 4 gün → GenelToplam 400 (KDV dahil)
    };

    [Fact]
    public async Task Invoice_from_rental_debits_cari_balanced_with_kdv()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid()));

        var invId = await invoices.CreateFromRentalAsync(rentalId);
        var inv = await invoices.GetAsync(invId);

        Assert.Equal("FT-000001", inv!.No);
        Assert.Equal(400m, inv.GenelToplam);
        Assert.Equal(333.33m, inv.NetTutar);
        Assert.Equal(66.67m, inv.KdvTutar);
        Assert.NotNull(inv.EFaturaEttn);           // e-Fatura stub ETTN
        Assert.True(inv.EFaturaGonderildi);
        Assert.Single(inv.Lines);

        // Borç Cari 400 → cari bakiye +400 (müşteri borçlu).
        Assert.Equal(400m, await cash.GetCariBalanceAsync(cari));

        // Defter dengeli: 1 borç (Cari 400) + 2 alacak (Gelir 333.33 + KDV 66.67) = 400.
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Fatura").ToListAsync();
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task Invoice_then_payment_settles_cari_to_zero()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid()));
        await invoices.CreateFromRentalAsync(rentalId);          // Borç 400
        await cash.CollectAsync(new CashInput { CariId = cari, RentalId = rentalId, Tutar = 400m }); // Alacak 400

        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));  // mahsuplaşır
    }

    [Fact]
    public async Task Invoice_is_immutable_at_db_level()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        var rentalId = await rentals.CreateDirectAsync(Rental(Guid.NewGuid(), Guid.NewGuid()));
        await invoices.CreateFromRentalAsync(rentalId);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var inv = await db.Invoices.FirstAsync();
        inv.GenelToplam = 1m; // kurcalama
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
