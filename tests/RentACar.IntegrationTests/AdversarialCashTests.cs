using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Adversarial probes for PR #26 (Kasa/Banka). Expected values derived by hand from
/// the INTENDED semantics, not from the service code. Goal: REFUTE correctness.
/// </summary>
[Collection("postgres")]
public sealed class AdversarialCashTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedCariAsync(IServiceScope scope, string ad)
    {
        var customers = scope.ServiceProvider.GetRequiredService<CustomerService>();
        return await customers.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test" });
    }

    private static IDbContextFactory<AppDbContext> Factory(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    // ---- 1. Multi-currency: USD Odeme @ rate 30, balance & ledger in BASE ----
    [Fact]
    public async Task Odeme_USD_rate30_balances_in_base_and_moves_cari_by_base()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "UsdOdeme");

        // Ödeme 100 USD @30 = 3000 base. Borç Cari (+3000) / Alacak Kasa (−3000).
        await cash.PayAsync(new CashInput
        {
            CariId = cari, Tutar = 100m, Doviz = "USD", Kur = 30m, Hesap = LedgerAccountType.Kasa
        });

        Assert.Equal(3000m, await cash.GetCariBalanceAsync(cari));
        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(3000m, s.KasaCikis);
        Assert.Equal(-3000m, s.KasaBakiye);

        // Verify ledger is balanced in base directly.
        await using var db = await Factory(scope).CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        var debit = rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase);
        var credit = rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase);
        Assert.Equal(debit, credit);
    }

    // ---- 2. Multi-currency reversal: reverse a USD Odeme zeroes cari in base ----
    [Fact]
    public async Task Reverse_USD_odeme_zeroes_cari_and_kasa_in_base()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "UsdTers");

        var id = await cash.PayAsync(new CashInput
        {
            CariId = cari, Tutar = 100m, Doviz = "USD", Kur = 30m, Hesap = LedgerAccountType.Kasa
        });
        Assert.Equal(3000m, await cash.GetCariBalanceAsync(cari));

        await cash.ReverseAsync(id);

        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(3000m, s.KasaGiris);
        Assert.Equal(3000m, s.KasaCikis);
        Assert.Equal(0m, s.KasaBakiye);
    }

    // ---- 3. Double-reversal must be rejected (idempotent) ----
    [Fact]
    public async Task Cannot_reverse_same_odeme_twice()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "DoubleRev");

        var id = await cash.PayAsync(new CashInput { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa });
        await cash.ReverseAsync(id);
        await Assert.ThrowsAsync<ValidationException>(() => cash.ReverseAsync(id));

        // Net cari must still be exactly zero, not double-zeroed.
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
    }

    // ---- 3b. Cannot reverse a reversal ----
    [Fact]
    public async Task Cannot_reverse_a_reversal()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "RevRev");

        var id = await cash.PayAsync(new CashInput { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa });
        var revId = await cash.ReverseAsync(id);
        await Assert.ThrowsAsync<ValidationException>(() => cash.ReverseAsync(revId));
    }

    // ---- 3c. Concurrent double-reversal: partial unique index must catch the race ----
    [Fact]
    public async Task Concurrent_reversal_only_one_succeeds()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash0 = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "RaceRev");
        var id = await cash0.PayAsync(new CashInput { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa });

        // Two independent scopes (independent DbContexts) reversing at the same time.
        var tenant = scope.ServiceProvider.GetRequiredService<TestIdentity>().TenantId!.Value;
        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        var c1 = s1.ServiceProvider.GetRequiredService<CashService>();
        var c2 = s2.ServiceProvider.GetRequiredService<CashService>();

        var t1 = Task.Run(() => c1.ReverseAsync(id));
        var t2 = Task.Run(() => c2.ReverseAsync(id));
        var results = await Task.WhenAll(
            Wrap(t1), Wrap(t2));

        // Exactly one succeeds.
        Assert.Equal(1, results.Count(r => r.ok));
        Assert.Equal(0m, await cash0.GetCariBalanceAsync(cari));

        static async Task<(bool ok, string? err)> Wrap(Task t)
        {
            try { await t; return (true, null); }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }

    // ---- 4. Rental-coupled Odeme: rental.Tahsilat/Bakiye direction + reversal ----
    [Fact]
    public async Task Odeme_on_rental_decreases_tahsilat_reversal_restores()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "RentalOdeme");

        // Seed a rental with GenelToplam 5000, already Tahsilat 5000, Bakiye 0.
        var rentalId = Guid.NewGuid();
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            db.Rentals.Add(new RentalContract
            {
                Id = rentalId, SozlesmeNo = "KS-TEST1", MusteriId = cari, VehicleId = Guid.NewGuid(),
                BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(1),
                GenelToplam = 5000m, Tahsilat = 5000m, Bakiye = 0m
            });
            await db.SaveChangesAsync();
        }

        // Ödeme (refund) 1000 on the rental → Tahsilat 4000, Bakiye 1000.
        var id = await cash.PayAsync(new CashInput
        {
            CariId = cari, RentalId = rentalId, Tutar = 1000m, Hesap = LedgerAccountType.Kasa
        });

        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
            Assert.Equal(4000m, r.Tahsilat);
            Assert.Equal(1000m, r.Bakiye);
        }

        // Reversal restores Tahsilat 5000, Bakiye 0.
        await cash.ReverseAsync(id);
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
            Assert.Equal(5000m, r.Tahsilat);
            Assert.Equal(0m, r.Bakiye);
        }
    }

    // ---- 4b. Rental-coupled Tahsilat reversal (control) ----
    [Fact]
    public async Task Tahsilat_on_rental_increases_then_reversal_restores()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "RentalTahsilat");

        var rentalId = Guid.NewGuid();
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            db.Rentals.Add(new RentalContract
            {
                Id = rentalId, SozlesmeNo = "KS-TEST2", MusteriId = cari, VehicleId = Guid.NewGuid(),
                BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(1),
                GenelToplam = 5000m, Tahsilat = 0m, Bakiye = 5000m
            });
            await db.SaveChangesAsync();
        }

        var id = await cash.CollectAsync(new CashInput
        {
            CariId = cari, RentalId = rentalId, Tutar = 2000m, Hesap = LedgerAccountType.Kasa
        });
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
            Assert.Equal(2000m, r.Tahsilat);
            Assert.Equal(3000m, r.Bakiye);
        }

        await cash.ReverseAsync(id);
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
            Assert.Equal(0m, r.Tahsilat);
            Assert.Equal(5000m, r.Bakiye);
        }
    }

    // ---- 5. Tenant isolation of virman ----
    [Fact]
    public async Task Virman_does_not_leak_across_tenants()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var a = host.ScopeFor(tenantA))
        {
            var cashA = a.ServiceProvider.GetRequiredService<CashService>();
            var cariA = await SeedCariAsync(a, "A");
            await cashA.CollectAsync(new CashInput { CariId = cariA, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });
            await cashA.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 400m);
        }

        // Tenant B sees nothing.
        using var b = host.ScopeFor(tenantB);
        var reportsB = b.ServiceProvider.GetRequiredService<ReportService>();
        var s = await reportsB.GetKasaBankaSummaryAsync();
        Assert.Equal(0m, s.KasaGiris);
        Assert.Equal(0m, s.KasaCikis);
        Assert.Equal(0m, s.BankaGiris);
        Assert.Equal(0m, s.KasaBakiye);
        Assert.Equal(0m, s.BankaBakiye);
    }

    // ---- 6. Permission enforced on Transfer & Reverse ----
    [Fact]
    public async Task NonFinance_user_cannot_transfer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m));
    }

    [Fact]
    public async Task NonFinance_user_cannot_reverse()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id;
        using (var admin = host.ScopeFor(tenant))
        {
            var cashA = admin.ServiceProvider.GetRequiredService<CashService>();
            var cari = await SeedCariAsync(admin, "RevPerm");
            id = await cashA.PayAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        }
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(() => cash.ReverseAsync(id));
    }

    // ---- 5b. Cross-tenant reversal: tenant B must NOT be able to reverse tenant A's tx ----
    [Fact]
    public async Task Tenant_B_cannot_reverse_tenant_A_transaction()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid id;
        Guid cariA;
        using (var a = host.ScopeFor(tenantA))
        {
            var cashA = a.ServiceProvider.GetRequiredService<CashService>();
            cariA = await SeedCariAsync(a, "AOwner");
            id = await cashA.PayAsync(new CashInput { CariId = cariA, Tutar = 700m, Hesap = LedgerAccountType.Kasa });
        }

        using var b = host.ScopeFor(tenantB);
        var cashB = b.ServiceProvider.GetRequiredService<CashService>();
        // FindAsync is tenant-filtered → B should get "not found", never reverse A's money.
        await Assert.ThrowsAsync<ValidationException>(() => cashB.ReverseAsync(id));

        // A's balance is untouched and still reversible by A.
        using var a2 = host.ScopeFor(tenantA);
        var cashA2 = a2.ServiceProvider.GetRequiredService<CashService>();
        Assert.Equal(700m, await cashA2.GetCariBalanceAsync(cariA));
    }

    // ---- 4. Guard: FX with high-precision rate stays balanced (both legs same Money) ----
    [Fact]
    public async Task Ledger_balanced_under_high_precision_rate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "Precision");

        // Rate with many decimals: 33.333333. 100 * that = 3333.3333 base.
        await cash.PayAsync(new CashInput
        {
            CariId = cari, Tutar = 100m, Doviz = "USD", Kur = 33.333333m, Hesap = LedgerAccountType.Banka
        });

        await using var db = await Factory(scope).CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        var debit = rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase);
        var credit = rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase);
        Assert.Equal(debit, credit);
        Assert.Equal(3333.3333m, await cash.GetCariBalanceAsync(cari));
    }

    // ---- 7. Virman FX: does kur=1 hardcode in endpoint silently lose value? ----
    // TransferAsync allows a kur param, but for a same-currency cash->bank transfer
    // value must be conserved. Probe a non-TRY virman with explicit rate.
    [Fact]
    public async Task Virman_non_try_conserves_total_in_base()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        // 100 USD @30 kasa->banka. Both legs same Money → base conserved.
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m, "USD", 30m);
        var s = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(3000m, s.BankaGiris);
        Assert.Equal(3000m, s.KasaCikis);
        Assert.Equal(0m, s.KasaBakiye + s.BankaBakiye);
    }
}
