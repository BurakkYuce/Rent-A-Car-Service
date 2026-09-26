using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Integrations;
using RentACar.Application.Penalties;
using RentACar.Application.Periods;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap D2 — Dönem kapanışı. BAĞIMSIZ ORACLE: kilit tarihine/öncesine HER postlama yolu reddedilir,
/// açık tarihe serbest. İki sınıf: (A) geri-tarihli yollar (Collect/Pay/Expense + batch'ler), (B) bugün-
/// tarihli yollar (Transfer/CariVirman/Fatura/Ceza yansıt/HGS/Ters) — kilit geleceğe konup bugün kapanır.
/// </summary>
[Collection("postgres")]
public sealed class DonemKapanisTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset LockKey = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Closed = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero); // <= kilit
    private static readonly DateTimeOffset Open = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);   // > kilit

    private static CashInput Cash(Guid account, DateTimeOffset date) => new()
    { CariId = account, Tutar = 100m, Kur = 1m, Doviz = "TRY", Hesap = LedgerAccountType.Kasa, Tarih = date };

    private static ExpenseInput Exp(DateTimeOffset date) => new()
    { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = PaymentMethod.Nakit, Tarih = date };

    [Fact]
    public async Task Backdated_postings_into_closed_period_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Kilit Test" });
        var cash = sp.GetRequiredService<CashService>();
        var exp = sp.GetRequiredService<ExpenseService>();

        await sp.GetRequiredService<PeriodLockService>().LockAsync(LockKey);

        // KAPALI tarih → red (her geri-tarihli yol)
        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(Cash(customerId, Closed)));
        await Assert.ThrowsAsync<ValidationException>(() => cash.PayAsync(Cash(customerId, Closed)));
        await Assert.ThrowsAsync<ValidationException>(() => exp.CreateAsync(Exp(Closed)));
        await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync([Cash(customerId, Closed)]));
        await Assert.ThrowsAsync<ValidationException>(() => exp.BatchCreateAsync([Exp(Closed)]));

        // AÇIK tarih → serbest
        Assert.NotEqual(Guid.Empty, await cash.CollectAsync(Cash(customerId, Open)));
        Assert.NotEqual(Guid.Empty, await exp.CreateAsync(Exp(Open)));
    }

    [Fact]
    public async Task NowDated_postings_blocked_when_today_locked_and_unlock_frees()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Kilitsiz hazırlık (postlama-öncesi kayıtlar serbest).
        var cs = sp.GetRequiredService<CustomerService>();
        var account1 = await cs.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Cari Bir" });
        var account2 = await cs.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Cari İki" });
        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 DK 01" });
        var rId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = account1, VehicleId = vId, BasTar = Open, BitTar = Open.AddDays(4), GunlukUcret = 100m });
        var cash = sp.GetRequiredService<CashService>();
        var txId = await cash.CollectAsync(Cash(account1, Open)); // ters kayıt için
        var pId = await sp.GetRequiredService<PenaltyService>().CreateAsync(new PenaltyInput
        { CezaTuru = "Hız", Tutar = 500m, VadeGun = 30, CariId = account1, VehicleId = vId });

        // Bugünü kapsayan kilit → bugün-tarihli postlamalar kapalı.
        await sp.GetRequiredService<PeriodLockService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenAccountsAsync(account1, account2, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.ReverseAsync(txId));
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rId));
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<PenaltyService>().ReflectAsync(pId));

        var hgs = new HgsReflectionService(
            new FakeHgs([new TollCrossing(Open, "Köprü", 100m)]),
            sp.GetRequiredService<ILedgerPoster>(), sp.GetRequiredService<IPeriodLockGuard>(), sp.GetRequiredService<RentACar.Domain.Common.ICurrentUser>());
        await Assert.ThrowsAsync<ValidationException>(() => hgs.ReflectAsync(account1, "34DK01", Open, Open.AddDays(1)));

        // Kilidi kaldır → bugün-tarihli postlama yeniden serbest.
        await sp.GetRequiredService<PeriodLockService>().UnlockAsync();
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m); // exception YOK
    }

    [Fact]
    public async Task Vehicle_sale_backdated_into_closed_rejected() // adversarial CRITICAL düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Satış Alıcı" });
        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 DS 01" });
        var sales = sp.GetRequiredService<VehicleSaleService>();

        await sp.GetRequiredService<PeriodLockService>().LockAsync(LockKey);

        VehicleSaleInput Sale(DateTimeOffset t) => new()
        { VehicleId = vId, AliciCariId = customerId, SatisNet = 100m, KdvOrani = 0.20m, Doviz = "TRY", Kur = 1m, Tarih = t };

        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(Sale(Closed))); // kapalı → red
        Assert.NotEqual(Guid.Empty, await sales.CreateAsync(Sale(Open)));                     // açık → serbest
    }

    private sealed class FakeHgs(IReadOnlyList<TollCrossing> crossings) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(string plate, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(crossings);
    }

    [Fact]
    public async Task No_lock_allows_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Serbest" });

        // Kilit yok → geri-tarihli bile serbest.
        Assert.NotEqual(Guid.Empty, await sp.GetRequiredService<CashService>().CollectAsync(Cash(customerId, Closed)));
    }
}
