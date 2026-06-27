using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class ExpenseTests(PostgresFixture fx)
{
    [Fact]
    public async Task Cash_expense_posts_balanced_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var id = await expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = Guid.NewGuid(),
            NetTutar = 100m, KdvOrani = 0.20m, OdemeYontemi = OdemeYontemi.Nakit
        });

        var exp = await expenses.GetAsync(id);
        Assert.Equal("GD-000001", exp!.No);
        Assert.Equal(20m, exp.KdvTutar);
        Assert.Equal(120m, exp.GenelToplam);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Gider").ToListAsync();
        Assert.Equal(3, entries.Count); // Borç Gider + Borç KDV + Alacak Kasa
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(120m, debit);   // 100 Gider + 20 KDV
        Assert.Equal(120m, credit);  // 120 Kasa
        Assert.Equal(debit, credit); // dengeli
        Assert.Equal(LedgerAccountType.Kasa, entries.Single(e => e.Direction == LedgerDirection.Credit).AccountType);
    }

    [Fact]
    public async Task Supplier_credit_expense_increases_supplier_payable()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var tedarikci = Guid.NewGuid();

        await expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, CariId = tedarikci,
            NetTutar = 200m, KdvOrani = 0.20m, OdemeYontemi = OdemeYontemi.AcikHesap
        });

        // Alacak Cari (gross 240) → bakiye -240 (tedarikçiye borçluyuz / alacaklı).
        Assert.Equal(-240m, await cash.GetCariBalanceAsync(tedarikci));
    }

    [Fact]
    public async Task Validation_rejects_bad_input()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ExpenseInput { NetTutar = 0m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new ExpenseInput { NetTutar = 10m, OdemeYontemi = OdemeYontemi.AcikHesap })); // cari yok
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new ExpenseInput { NetTutar = 10m, Tip = ExpenseType.Arac })); // araç yok
    }

    [Fact]
    public async Task Expense_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<ExpenseService>()
                .CreateAsync(new ExpenseInput { NetTutar = 50m, OdemeYontemi = OdemeYontemi.Nakit });

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ExpenseService>().ListAsync());
    }

    [Fact]
    public async Task Expense_writes_audit_and_is_immutable()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "auditor");
        await scope.ServiceProvider.GetRequiredService<ExpenseService>()
            .CreateAsync(new ExpenseInput { NetTutar = 75m, OdemeYontemi = OdemeYontemi.Nakit });

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var audit = Assert.Single(await db.AuditLogs.Where(a => a.EntityName == "Expenses").ToListAsync());
        Assert.Equal("auditor", audit.UserName);

        var exp = await db.Expenses.FirstAsync();
        exp.Aciklama = "tahrif";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync()); // immutable
    }
}
