using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira dövizi → fatura → LEDGER otomatik TL (Money.AmountInBase = amount×Kur). BAĞIMSIZ ORACLE:
/// EUR kira 100/gün × 3 gün = 300 EUR; firma sabit kur 40 → cari TL bakiye 300×40 = 12000. Para-kritik:
/// kur yönü (EUR sayısı > TL değil, TL karşılığı BÜYÜK), defter dengesi (×Kur sonrası), TL kira değişmez.
/// </summary>
[Collection("postgres")]
public sealed class KiraFaturaDovizTests(PostgresFixture fx)
{
    private static BookingInput Rental(Guid cari, Guid vehicle, string? doviz) => new()
    {
        MusteriId = cari,
        VehicleId = vehicle,
        BasTar = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero),
        BitTar = new DateTimeOffset(2026, 12, 4, 9, 0, 0, TimeSpan.Zero), // 3 gün
        GunlukUcret = 100m,
        Doviz = doviz
    };

    [Fact]
    public async Task EUR_kira_faturasi_ledger_TL_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var sabit = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

        // Firma EUR'yu 40 TL sabitler (deterministik — TCMB'ye bağımlı olma; override çözümlenir).
        await sabit.UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });

        var cari = await TestCari.YeniAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, await TestArac.YeniAsync(scope.ServiceProvider), "EURO")); // form değeri "EURO"
        var invId = await invoices.CreateFromRentalAsync(rentalId);
        var inv = await invoices.GetAsync(invId);

        // Fatura EUR cinsinden: 300 EUR brüt (3×100), Currency normalize "EUR", kur 40 yakalandı.
        Assert.Equal("EUR", inv!.Currency);
        Assert.Equal(40m, inv.Kur);
        Assert.Equal(300m, inv.GenelToplam);
        Assert.Equal(250m, inv.NetTutar); // 300/1.2
        Assert.Equal(50m, inv.KdvTutar);

        // LEDGER otomatik TL: cari bakiye = 300 EUR × 40 = 12000 TL (kur YÖNÜ doğru).
        Assert.Equal(12000m, await cash.GetAccountBalanceAsync(cari));

        // Denge: Σ borç(base) == Σ alacak(base) (hepsi ×40 sonrası).
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Fatura" && e.SourceId == invId).ToListAsync();
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
        Assert.Equal(12000m, borc);
        Assert.Equal(borc, alacak);
    }

    [Fact]
    public async Task TL_kira_degismez_kur_1()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var cari = await TestCari.YeniAsync(scope.ServiceProvider);
        var rentalId = await rentals.CreateDirectAsync(Rental(cari, await TestArac.YeniAsync(scope.ServiceProvider), "TL"));
        var invId = await invoices.CreateFromRentalAsync(rentalId);
        var inv = await invoices.GetAsync(invId);

        Assert.Equal("TRY", inv!.Currency);
        Assert.Equal(1m, inv.Kur);
        Assert.Equal(300m, inv.GenelToplam);
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(cari)); // TL 1:1, değişmedi
    }
}
