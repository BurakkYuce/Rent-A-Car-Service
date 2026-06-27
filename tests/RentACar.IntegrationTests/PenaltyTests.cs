using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Integrations;
using RentACar.Application.Penalties;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class PenaltyTests(PostgresFixture fx)
{
    /// <summary>Sabit geçiş listesi döndüren test double (HGS port'u).</summary>
    private sealed class FakeHgsService(IReadOnlyList<TollCrossing> crossings) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(
            string plaka, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(crossings);
    }

    [Fact]
    public async Task Create_computes_vade_and_gapless_no()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var teblig = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", TebligTarihi = teblig, VadeGun = 15, Tutar = 500m
        });

        var p = await svc.GetAsync(id);
        Assert.Equal("CZ-000001", p!.No);
        Assert.Equal(teblig.AddDays(15), p.VadeTarihi);
        Assert.Equal(CezaDurum.Yeni, p.Durum);

        // İkinci ceza boşluksuz devam eder.
        var id2 = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Park", Tutar = 100m });
        Assert.Equal("CZ-000002", (await svc.GetAsync(id2))!.No);
    }

    [Fact]
    public async Task Yansit_posts_balanced_ledger_and_debits_cari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = cari, Tutar = 750m });
        Assert.True(await svc.YansitAsync(id));

        Assert.Equal(CezaDurum.Yansitildi, (await svc.GetAsync(id))!.Durum);
        // Borç Cari 750 → müşteri borçlu (+750).
        Assert.Equal(750m, await cash.GetCariBalanceAsync(cari));

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
        var cari = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = cari, Tutar = 300m });
        Assert.True(await svc.YansitAsync(id));
        // İkinci yansıtma → zaten Yansitildi → ValidationException (servis guard'ı).
        await Assert.ThrowsAsync<ValidationException>(() => svc.YansitAsync(id));

        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        Assert.Equal(300m, await cash.GetCariBalanceAsync(cari)); // çift borçlanma yok

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

        var noCari = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 100m });
        await Assert.ThrowsAsync<ValidationException>(() => svc.YansitAsync(noCari));

        var paid = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = Guid.NewGuid(), Tutar = 100m });
        await svc.OdeAsync(paid);
        await Assert.ThrowsAsync<ValidationException>(() => svc.YansitAsync(paid)); // Odendi → yansıtılamaz
    }

    [Fact]
    public async Task Hgs_reflection_posts_balanced_with_service_margin()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var poster = scope.ServiceProvider.GetRequiredService<ILedgerPoster>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var fake = new FakeHgsService([
            new TollCrossing(t, "Köprü", 100m),
            new TollCrossing(t.AddHours(1), "OGS", 50m)
        ]);
        var hgs = new HgsReflectionService(fake, poster);

        // toplam 150 * 1.03 = 154.50 → cari borçlanır.
        var result = await hgs.ReflectAsync(cari, "34ABC34", t, t.AddDays(1));
        Assert.Equal(2, result.GecisSayisi);
        Assert.Equal(150m, result.ToplamGecis);
        Assert.Equal(154.50m, result.YansitilanTutar);
        Assert.Equal(154.50m, await cash.GetCariBalanceAsync(cari));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Hgs").ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(
            entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
            entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));
    }

    [Fact]
    public async Task Hgs_reflection_is_idempotent_on_retry()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var poster = scope.ServiceProvider.GetRequiredService<ILedgerPoster>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        var t = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var fake = new FakeHgsService([new TollCrossing(t, "Köprü", 100m)]);
        var hgs = new HgsReflectionService(fake, poster);

        // Aynı (cari, plaka, dönem) iki kez yansıt → DETERMİNİSTİK SourceId → ikinci no-op.
        await hgs.ReflectAsync(cari, "34ABC34", t, t.AddDays(1));
        await hgs.ReflectAsync(cari, "34ABC34", t, t.AddDays(1));

        // 103.00 yalnız BİR kez borçlanmalı (çift faturalama yok).
        Assert.Equal(103m, await cash.GetCariBalanceAsync(cari));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Hgs").CountAsync());
    }

    [Fact]
    public async Task Hgs_reflection_different_period_posts_again()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var poster = scope.ServiceProvider.GetRequiredService<ILedgerPoster>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = Guid.NewGuid();

        var t1 = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var hgs = new HgsReflectionService(new FakeHgsService([new TollCrossing(t1, "Köprü", 100m)]), poster);

        await hgs.ReflectAsync(cari, "34ABC34", t1, t1.AddDays(1)); // Şubat dönemi
        await hgs.ReflectAsync(cari, "34ABC34", t2, t2.AddDays(1)); // Mart dönemi → ayrı

        Assert.Equal(206m, await cash.GetCariBalanceAsync(cari)); // 103 + 103, meşru iki dönem
    }

    [Fact]
    public async Task Hgs_reflection_with_no_crossings_is_noop()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var poster = scope.ServiceProvider.GetRequiredService<ILedgerPoster>();
        var hgs = new HgsReflectionService(new FakeHgsService([]), poster);

        var result = await hgs.ReflectAsync(Guid.NewGuid(), "34XYZ34", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.Equal(0, result.GecisSayisi);
        Assert.Equal(0m, result.YansitilanTutar);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Hgs").ToListAsync());
    }

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
        var cari = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = cari, Tutar = 200m });
        await svc.YansitAsync(id);          // başlık güncellenir (Yeni → Yansitildi)
        Assert.True(await svc.OdeAsync(id)); // başlık tekrar güncellenir (→ Odendi)

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
