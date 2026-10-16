using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Penalties;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class PenaltyTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_computes_vade_and_gapless_no()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var notification = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", TebligTarihi = notification, VadeGun = 15, Tutar = 500m
        });

        var p = await svc.GetAsync(id);
        DocumentNoOracle.OneOfExpected(8, 1, p!.No);
        Assert.Equal(notification.AddDays(15), p.VadeTarihi);
        Assert.Equal(PenaltyStatus.Yeni, p.Durum);

        // İkinci ceza boşluksuz devam eder.
        var id2 = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Park", Tutar = 100m });
        DocumentNoOracle.OneOfExpected(8, 2, (await svc.GetAsync(id2))!.No);
    }

    [Fact]
    public async Task Yansit_posts_balanced_ledger_and_debits_cari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = account, Tutar = 750m });
        Assert.True(await svc.ReflectAsync(id));

        Assert.Equal(PenaltyStatus.Yansitildi, (await svc.GetAsync(id))!.Durum);
        // Borç Cari 750 → müşteri borçlu (+750).
        Assert.Equal(750m, await cash.GetAccountBalanceAsync(account));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Ceza").ToListAsync();
        Assert.Equal(2, entries.Count); // Borç Cari + Alacak Gelir
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(750m, debit);
        Assert.Equal(debit, credit); // dengeli
        Assert.Equal(LedgerAccountType.Gelir, entries.Single(e => e.Direction == LedgerDirection.Credit).AccountType);
    }

    [Fact]
    public async Task Yansit_is_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();
        var account = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = account, Tutar = 300m });
        Assert.True(await svc.ReflectAsync(id));
        // İkinci yansıtma → zaten Yansitildi → ValidationException (servis guard'ı).
        await Assert.ThrowsAsync<ValidationException>(() => svc.ReflectAsync(id));

        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(account)); // çift borçlanma yok

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Ceza").CountAsync());
    }

    [Fact]
    public async Task Yansit_requires_cari_and_new_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var noCustomer = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 100m });
        await Assert.ThrowsAsync<ValidationException>(() => svc.ReflectAsync(noCustomer));

        var paid = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = Guid.NewGuid(), Tutar = 100m });
        await svc.PayAsync(paid);
        await Assert.ThrowsAsync<ValidationException>(() => svc.ReflectAsync(paid)); // Odendi → yansıtılamaz
    }

    // HGS yansıtma testleri (dönem-MD5'li HgsReflectionService) #391 L1 ile servisle birlikte kaldırıldı; geçiş başına
    // yansıtma (dengeli defter, hizmet oranı, idempotency, eşzamanlılık) HgsImportTests + HgsAdversarialProbeTests'te.

    [Fact]
    public async Task Penalty_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<PenaltyService>()
                .CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 100m });

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<PenaltyService>().ListAsync());
    }

    [Fact]
    public async Task Penalty_header_updatable_but_reflection_ledger_immutable()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "memur");
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();
        var account = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = account, Tutar = 200m });
        await svc.ReflectAsync(id);          // başlık güncellenir (Yeni → Yansitildi)
        Assert.True(await svc.PayAsync(id)); // başlık tekrar güncellenir (→ Odendi)

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // Başlık audit'i yazıldı.
        Assert.NotEmpty(await db.AuditLogs.Where(a => a.EntityName == "Penalties").ToListAsync());

        // Yansıtma defteri DB-immutable.
        var entry = await db.AccountLedgerEntries.FirstAsync(e => e.SourceType == "Ceza");
        entry.Description = "tahrif";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
