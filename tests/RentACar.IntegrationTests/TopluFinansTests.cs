using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Toplu tahsilat/gider (parite #10) — BAĞIMSIZ ORACLE. ATOMİKLİK (hep-ya-hiç), satır-bazlı denge,
/// idempotency (işlem anahtarı), boş batch reddi, tenant izolasyon. Beklenenler senaryodan.
/// </summary>
[Collection("postgres")]
public sealed class TopluFinansTests(PostgresFixture fx)
{
    private static CashInput Row(Guid cari, decimal tutar) => new()
    { CariId = cari, Tutar = tutar, Doviz = "TRY", Kur = 1m, Hesap = LedgerAccountType.Kasa, Aciklama = "Toplu" };

    // Toplu yol artık cari varlığını denetler (tekil yolla aynı kural) → satırlar GERÇEK cari ister.
    private static Task<Guid> CariAsync(IServiceProvider sp, string ad) =>
        sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad });

    // ---- Toplu tahsilat ----

    [Fact]
    public async Task Batch_collect_posts_all_rows_balanced()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await CariAsync(scope.ServiceProvider, "A");
        var b = await CariAsync(scope.ServiceProvider, "B");
        var c = await CariAsync(scope.ServiceProvider, "C");

        await cash.BatchCollectAsync([Row(a, 1500m), Row(b, 2000m), Row(c, 500m)]);

        // Tahsilat → cari Alacak → bakiye negatif (senaryodan: −tutar her cari).
        Assert.Equal(-1500m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(-2000m, await cash.GetCariBalanceAsync(b));
        Assert.Equal(-500m, await cash.GetCariBalanceAsync(c));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Tahsilat").ToListAsync();
        Assert.Equal(6, entries.Count); // 3 satır × 2 kayıt
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        Assert.Equal(borc, alacak);
        Assert.Equal(4000m, borc);
        // 3 ayrı No tahsis edildi (boşluksuz, benzersiz).
        var nos = await db.CashTransactions.AsNoTracking().Select(t => t.No).ToListAsync();
        Assert.Equal(3, nos.Distinct().Count());
    }

    [Fact]
    public async Task Batch_is_atomic_one_bad_row_posts_nothing()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await CariAsync(scope.ServiceProvider, "A");

        // 2. satır geçersiz (boş cari) → TÜM batch reddedilir, hiçbir şey yazılmaz.
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.BatchCollectAsync([Row(a, 1500m), Row(Guid.Empty, 100m)]));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(a));

        // 2. satır tutar 0 → yine hep-ya-hiç.
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.BatchCollectAsync([Row(a, 1500m), Row(Guid.NewGuid(), 0m)]));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(a));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.CashTransactions.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Batch_collect_is_idempotent_with_key()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await CariAsync(scope.ServiceProvider, "A");
        var key = Guid.NewGuid();

        await cash.BatchCollectAsync([Row(a, 1000m)], key);
        // Aynı anahtarla çift-submit → ikinci tüm batch'i geri alır (çift sayım yok).
        await Assert.ThrowsAsync<MukerrerIslemException>(() => cash.BatchCollectAsync([Row(a, 1000m)], key));

        Assert.Equal(-1000m, await cash.GetCariBalanceAsync(a)); // tek virman
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.CashTransactions.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Empty_batch_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync([]));
    }

    [Fact]
    public async Task NonFinance_user_cannot_batch()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<YetkiYokException>(() => cash.BatchCollectAsync([Row(Guid.NewGuid(), 100m)]));
    }

    // ---- Cari varlığı (DEVIR §6 Low: Blazor BatchCollect/BatchPay cari kontrolü yoktu) ----

    private static async Task<(int Tx, int Ledger)> RowCountsAsync(IServiceProvider sp)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return (await db.CashTransactions.AsNoTracking().CountAsync(), await db.AccountLedgerEntries.AsNoTracking().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_with_nonexistent_cari_rejected_nothing_written(bool pay)
    {
        using var host = new TestHost(fx.AppConnectionString); // racar_app (RLS)
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var ghost = Guid.NewGuid(); // hiç var olmayan cari

        IReadOnlyList<CashInput> rows = [Row(a, 1500m), Row(ghost, 100m)];
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => pay ? cash.BatchPayAsync(rows, Guid.NewGuid()) : cash.BatchCollectAsync(rows, Guid.NewGuid()));
        Assert.Equal("satirlar[1].cariId", ex.Alan);
        Assert.Contains("Cari bulunamadı", ex.Message);
        Assert.StartsWith("Satır 2:", ex.Message);

        // Hep-ya-hiç: geçerli 1. satır da yazılmadı — ne belge ne defter satırı.
        Assert.Equal((0, 0), await RowCountsAsync(sp));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(a));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_with_other_tenants_cari_rejected(bool pay)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid();
        Guid foreign;
        using (var s2 = host.ScopeFor(t2)) foreign = await CariAsync(s2.ServiceProvider, "Yabancı");

        using var s1 = host.ScopeFor(t1);
        var sp = s1.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var own = await CariAsync(sp, "Kendi");

        IReadOnlyList<CashInput> rows = [Row(foreign, 250m), Row(own, 100m)];
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => pay ? cash.BatchPayAsync(rows) : cash.BatchCollectAsync(rows));
        Assert.Equal("satirlar[0].cariId", ex.Alan);
        Assert.Equal((0, 0), await RowCountsAsync(sp));

        // t2 tarafında da hiçbir şey yazılmadı.
        using var check = host.ScopeFor(t2);
        Assert.Equal((0, 0), await RowCountsAsync(check.ServiceProvider));
    }

    [Fact]
    public async Task Batch_pay_valid_caris_posts_balanced()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var b = await CariAsync(sp, "B");

        // Aynı cari iki satırda (tekrarlı kimlik) da geçerli.
        await cash.BatchPayAsync([Row(a, 300m), Row(b, 200m), Row(a, 50m)]);

        // Ödeme → Borç Cari → bakiye artar. ELLE: a = 300 + 50, b = 200; defter 3 × 2 satır, taraf toplamı 550.
        Assert.Equal(350m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(200m, await cash.GetCariBalanceAsync(b));
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        Assert.Equal(6, entries.Count);
        Assert.Equal(550m, entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate));
        Assert.Equal(550m, entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate));
    }

    // ---- Toplu gider ----

    private static ExpenseInput Gider(decimal net) => new()
    {
        Tip = ExpenseType.Genel, NetTutar = net, KdvOrani = 0.20m, Doviz = "TRY", Kur = 1m,
        OdemeYontemi = OdemeYontemi.Nakit, Aciklama = "Toplu gider"
    };

    [Fact]
    public async Task Batch_expense_posts_all_balanced()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var exp = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        await exp.BatchCreateAsync([Gider(1000m), Gider(500m)]);

        Assert.Equal(2, (await exp.ListAsync()).Count);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Gider").ToListAsync();
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        Assert.Equal(borc, alacak); // her kalem dengeli → toplam dengeli
    }

    [Fact]
    public async Task Batch_expense_is_atomic()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var exp = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => exp.BatchCreateAsync([Gider(1000m), Gider(0m)])); // 2. kalem net 0 → hep-ya-hiç
        Assert.Empty(await exp.ListAsync());
    }

    [Fact]
    public async Task Batch_expense_is_idempotent_with_key()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var exp = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var key = Guid.NewGuid();

        await exp.BatchCreateAsync([Gider(1000m)], key);
        await Assert.ThrowsAsync<MukerrerIslemException>(() => exp.BatchCreateAsync([Gider(1000m)], key));
        Assert.Single(await exp.ListAsync());
    }

    [Fact]
    public async Task Batch_collect_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid();
        Guid a;
        using (var s1 = host.ScopeFor(t1))
        {
            a = await CariAsync(s1.ServiceProvider, "A");
            await s1.ServiceProvider.GetRequiredService<CashService>().BatchCollectAsync([Row(a, 700m)]);
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Equal(0m, await s2.ServiceProvider.GetRequiredService<CashService>().GetCariBalanceAsync(a));
    }
}
