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
    private static readonly DateTimeOffset Kilit = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Kapali = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero); // <= kilit
    private static readonly DateTimeOffset Acik = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);   // > kilit

    private static CashInput Cash(Guid cari, DateTimeOffset tarih) => new()
    { CariId = cari, Tutar = 100m, Kur = 1m, Doviz = "TRY", Hesap = LedgerAccountType.Kasa, Tarih = tarih };

    private static ExpenseInput Exp(DateTimeOffset tarih) => new()
    { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Tarih = tarih };

    [Fact]
    public async Task Backdated_postings_into_closed_period_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kilit Test" });
        var cash = sp.GetRequiredService<CashService>();
        var exp = sp.GetRequiredService<ExpenseService>();

        await sp.GetRequiredService<DonemKilidiService>().LockAsync(Kilit);

        // KAPALI tarih → red (her geri-tarihli yol)
        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(Cash(cariId, Kapali)));
        await Assert.ThrowsAsync<ValidationException>(() => cash.PayAsync(Cash(cariId, Kapali)));
        await Assert.ThrowsAsync<ValidationException>(() => exp.CreateAsync(Exp(Kapali)));
        await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync([Cash(cariId, Kapali)]));
        await Assert.ThrowsAsync<ValidationException>(() => exp.BatchCreateAsync([Exp(Kapali)]));

        // AÇIK tarih → serbest
        Assert.NotEqual(Guid.Empty, await cash.CollectAsync(Cash(cariId, Acik)));
        Assert.NotEqual(Guid.Empty, await exp.CreateAsync(Exp(Acik)));
    }

    [Fact]
    public async Task NowDated_postings_blocked_when_today_locked_and_unlock_frees()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Kilitsiz hazırlık (postlama-öncesi kayıtlar serbest).
        var cs = sp.GetRequiredService<CustomerService>();
        var cari1 = await cs.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Cari Bir" });
        var cari2 = await cs.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Cari İki" });
        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 DK 01" });
        var rId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari1, VehicleId = vId, BasTar = Acik, BitTar = Acik.AddDays(4), GunlukUcret = 100m });
        var cash = sp.GetRequiredService<CashService>();
        var txId = await cash.CollectAsync(Cash(cari1, Acik)); // ters kayıt için
        var pId = await sp.GetRequiredService<PenaltyService>().CreateAsync(new PenaltyInput
        { CezaTuru = "Hız", Tutar = 500m, VadeGun = 30, CariId = cari1, VehicleId = vId });

        // Bugünü kapsayan kilit → bugün-tarihli postlamalar kapalı.
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(cari1, cari2, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => cash.ReverseAsync(txId));
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rId));
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<PenaltyService>().YansitAsync(pId));

        var hgs = new HgsReflectionService(
            new FakeHgs([new TollCrossing(Acik, "Köprü", 100m)]),
            sp.GetRequiredService<ILedgerPoster>(), sp.GetRequiredService<IPeriodLockGuard>());
        await Assert.ThrowsAsync<ValidationException>(() => hgs.ReflectAsync(cari1, "34DK01", Acik, Acik.AddDays(1)));

        // Kilidi kaldır → bugün-tarihli postlama yeniden serbest.
        await sp.GetRequiredService<DonemKilidiService>().UnlockAsync();
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m); // exception YOK
    }

    [Fact]
    public async Task Vehicle_sale_backdated_into_closed_rejected() // adversarial CRITICAL düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Satış Alıcı" });
        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 DS 01" });
        var sales = sp.GetRequiredService<VehicleSaleService>();

        await sp.GetRequiredService<DonemKilidiService>().LockAsync(Kilit);

        VehicleSaleInput Sale(DateTimeOffset t) => new()
        { VehicleId = vId, AliciCariId = cariId, SatisNet = 100m, KdvOrani = 0.20m, Doviz = "TRY", Kur = 1m, Tarih = t };

        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(Sale(Kapali))); // kapalı → red
        Assert.NotEqual(Guid.Empty, await sales.CreateAsync(Sale(Acik)));                     // açık → serbest
    }

    private sealed class FakeHgs(IReadOnlyList<TollCrossing> crossings) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(string plaka, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(crossings);
    }

    [Fact]
    public async Task No_lock_allows_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Serbest" });

        // Kilit yok → geri-tarihli bile serbest.
        Assert.NotEqual(Guid.Empty, await sp.GetRequiredService<CashService>().CollectAsync(Cash(cariId, Kapali)));
    }
}
