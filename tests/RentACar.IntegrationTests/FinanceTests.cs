using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Para akışı testleri. Bağımsız oracle = çift-taraflı muhasebe matematiği
/// (bakiye = Σ işaretli base; dengeli; ters kayıt geri alır).
/// </summary>
[Collection("postgres")]
public sealed class FinanceTests(PostgresFixture fx)
{
    [Fact]
    public async Task Collect_credits_cari_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 500m });

        // Tahsilat → Alacak Cari → bakiye -500 (müşteri alacaklı/avans).
        Assert.Equal(-500m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Multi_currency_collect_uses_base_conversion()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Doviz = "USD", Kur = 30m });

        // 100 USD * 30 = 3000 base, Alacak → -3000.
        Assert.Equal(-3000m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Reversal_restores_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        var txId = await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 750m });
        Assert.Equal(-750m, await cash.GetCariBalanceAsync(cari));

        await cash.ReverseAsync(txId);
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Ledger_is_balanced_for_each_transaction()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await cash.CollectAsync(new CashInput { CariId = Guid.NewGuid(), Tutar = 250m, Doviz = "EUR", Kur = 35m });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(debit, credit);        // dengeli
        Assert.Equal(2, entries.Count);     // Kasa + Cari
    }

    [Fact]
    public async Task Payment_linked_to_rental_updates_contract_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rent = scope.ServiceProvider.GetRequiredService<RentalService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var cari = Guid.NewGuid();
        var rentalId = await rent.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = Guid.NewGuid(),
            BasTar = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            BitTar = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero),
            GunlukUcret = 100m // 4 gün → 400
        });

        await cash.CollectAsync(new CashInput { CariId = cari, RentalId = rentalId, Tutar = 400m });

        var rental = await rent.GetAsync(rentalId);
        Assert.Equal(400m, rental!.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);                 // sözleşme tahsil edildi
        Assert.Equal(-400m, await cash.GetCariBalanceAsync(cari)); // faturasız → cari alacaklı
    }

    [Fact]
    public async Task Ledger_entries_are_immutable_at_db_level()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await cash.CollectAsync(new CashInput { CariId = Guid.NewGuid(), Tutar = 100m });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.AccountLedgerEntries.FirstAsync();
        entry.Description = "değiştirme denemesi";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync()); // trigger engeller
    }

    [Fact]
    public async Task Double_reversal_is_rejected_and_balance_preserved()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        var txId = await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 500m });
        await cash.ReverseAsync(txId);
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));

        // İkinci ters kayıt reddedilmeli (idempotency) → bakiye bozulmaz.
        await Assert.ThrowsAsync<ValidationException>(() => cash.ReverseAsync(txId));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Audit_log_is_immutable_at_db_level()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // Tahsilat IAuditable → audit satırı yazılır.
        await scope.ServiceProvider.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = Guid.NewGuid(), Tutar = 100m });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var log = await db.AuditLogs.FirstAsync();
        log.UserName = "tahrif";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync()); // trigger engeller
    }

    [Fact]
    public async Task Cari_balance_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var cari = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = cari, Tutar = 999m });

        using var s2 = host.ScopeFor(t2);
        var balance = await s2.ServiceProvider.GetRequiredService<CashService>().GetCariBalanceAsync(cari);
        Assert.Equal(0m, balance); // T2 T1'in defterini göremez (RLS)
    }
}
