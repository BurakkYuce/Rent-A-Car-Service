using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Cari↔cari virman (parite #9) — BAĞIMSIZ ORACLE. Beklenen bakiyeler elle senaryodan: kaynak −tutar,
/// hedef +tutar, Σ=0. Yön (kaynak alacak / hedef borç), denge, self-transfer reddi, tutar>0, yetki,
/// tenant izolasyon (racar_app) doğrulanır.
/// </summary>
[Collection("postgres")]
public sealed class CariVirmanTests(PostgresFixture fx)
{
    [Fact]
    public async Task Transfer_moves_balance_source_to_target_balanced()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = Guid.NewGuid(); // kaynak
        var b = Guid.NewGuid(); // hedef

        await cash.TransferBetweenCariAsync(a, b, 250.00m);

        // Kaynak alacaklandı (−250), hedef borçlandı (+250); toplam 0 (dengeli).
        Assert.Equal(-250.00m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(250.00m, await cash.GetCariBalanceAsync(b));

        // İkinci virman birikir.
        await cash.TransferBetweenCariAsync(a, b, 100.00m);
        Assert.Equal(-350.00m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(350.00m, await cash.GetCariBalanceAsync(b));

        // Defter base bazında dengeli: Σ borç(base) == Σ alacak(base) (CariVirman kaynaklı).
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "CariVirman").ToListAsync();
        Assert.Equal(4, entries.Count); // 2 virman × 2 satır
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        Assert.Equal(borc, alacak);
        Assert.Equal(350.00m, borc);
        // Yön doğru: hedef(b) hep Debit, kaynak(a) hep Credit.
        Assert.All(entries.Where(e => e.AccountRef == b), e => Assert.Equal(LedgerDirection.Debit, e.Direction));
        Assert.All(entries.Where(e => e.AccountRef == a), e => Assert.Equal(LedgerDirection.Credit, e.Direction));
    }

    [Fact]
    public async Task Reverse_direction_transfer_cancels_out()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await cash.TransferBetweenCariAsync(a, b, 500.00m);   // a −500, b +500
        await cash.TransferBetweenCariAsync(b, a, 500.00m);   // düzeltme: ters yön → sıfırlanır
        Assert.Equal(0m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(b));
    }

    [Fact]
    public async Task Same_islem_anahtari_is_idempotent() // adversarial MEDIUM #1 düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var anahtar = Guid.NewGuid();

        // Aynı işlem anahtarıyla çift gönderim (çift tıklama/geri-gönder) → TEK virman.
        await cash.TransferBetweenCariAsync(a, b, 500.00m, islemAnahtari: anahtar);
        await cash.TransferBetweenCariAsync(a, b, 500.00m, islemAnahtari: anahtar);

        Assert.Equal(-500.00m, await cash.GetCariBalanceAsync(a)); // çift sayılmadı
        Assert.Equal(500.00m, await cash.GetCariBalanceAsync(b));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var count = await db.AccountLedgerEntries.AsNoTracking()
            .CountAsync(e => e.SourceType == "CariVirman");
        Assert.Equal(2, count); // 1 virman × 2 satır (4 değil)
    }

    [Fact]
    public async Task Different_keys_are_separate_transfers()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Farklı anahtar (veya anahtarsız) → ayrı virmanlar birikir.
        await cash.TransferBetweenCariAsync(a, b, 500.00m, islemAnahtari: Guid.NewGuid());
        await cash.TransferBetweenCariAsync(a, b, 500.00m, islemAnahtari: Guid.NewGuid());
        Assert.Equal(-1000.00m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(1000.00m, await cash.GetCariBalanceAsync(b));
    }

    [Fact]
    public async Task Self_transfer_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = Guid.NewGuid();
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(a, a, 100m));
    }

    [Fact]
    public async Task Nonpositive_amount_and_empty_cari_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(a, b, 0m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(a, b, -50m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(Guid.Empty, b, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(a, Guid.Empty, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(a, b, 100m, kur: 0m));
    }

    [Fact]
    public async Task NonFinance_user_cannot_transfer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.TransferBetweenCariAsync(Guid.NewGuid(), Guid.NewGuid(), 100m));
    }

    [Fact]
    public async Task Cari_virman_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CashService>().TransferBetweenCariAsync(a, b, 300m);

        // t2 aynı cari id'lerinin bakiyesini GÖRMEZ (RLS).
        using var s2 = host.ScopeFor(t2);
        var cash2 = s2.ServiceProvider.GetRequiredService<CashService>();
        Assert.Equal(0m, await cash2.GetCariBalanceAsync(a));
        Assert.Equal(0m, await cash2.GetCariBalanceAsync(b));
    }
}
