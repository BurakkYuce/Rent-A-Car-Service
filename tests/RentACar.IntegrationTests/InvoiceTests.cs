using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
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
    public async Task Tax_fields_persist_and_ledger_stays_balanced() // parite #8
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var cari = Guid.NewGuid();
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, Guid.NewGuid()));

        // Vergi metadata ile fatura kes (bilgi amaçlı — postlamayı değiştirmemeli).
        var invId = await invoices.CreateFromRentalAsync(rentalId, vergi: new InvoiceTaxInfo(
            Otv: 50.00m, TevkifatOran: 20.00m, TevkifatTutar: 13.33m, DamgaVergisi: 7.59m,
            IadeMi: true, ManuelMi: false));
        var inv = await invoices.GetAsync(invId);

        // Alanlar persist oldu.
        Assert.Equal(50.00m, inv!.Otv);
        Assert.Equal(20.00m, inv.TevkifatOran);
        Assert.Equal(13.33m, inv.TevkifatTutar);
        Assert.Equal(7.59m, inv.DamgaVergisi);
        Assert.True(inv.IadeMi);
        Assert.False(inv.ManuelMi);

        // KRİTİK: vergi alanları postlamayı DEĞİŞTİRMEDİ — toplamlar + cari bakiye aynı (400/333.33/66.67).
        Assert.Equal(400m, inv.GenelToplam);
        Assert.Equal(333.33m, inv.NetTutar);
        Assert.Equal(66.67m, inv.KdvTutar);
        Assert.Equal(400m, await cash.GetCariBalanceAsync(cari));

        // Defter base bazında DENGELİ: Σ Borç(base) == Σ Alacak(base), hâlâ 3 kayıt.
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Fatura").ToListAsync();
        Assert.Equal(3, entries.Count);
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        Assert.Equal(borc, alacak);
        Assert.Equal(400m, borc);
    }

    [Fact]
    public async Task Invalid_tax_field_rejected() // parite #8
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        var rentalId = await rentals.CreateDirectAsync(Rental(Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(
            () => invoices.CreateFromRentalAsync(rentalId, vergi: new InvoiceTaxInfo(Otv: -1m)));
        // Negatif vergi reddedildi → fatura POSTLANMADI; kira faturasız kalmalı (retry mümkün).
        var rentalId2 = await rentals.CreateDirectAsync(Rental(Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(
            () => invoices.CreateFromRentalAsync(rentalId2, vergi: new InvoiceTaxInfo(TevkifatOran: 150m)));
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
