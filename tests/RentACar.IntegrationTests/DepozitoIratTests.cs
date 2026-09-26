using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ1-1.2 — Depozito İRAT (kullanıcı kararı: irat GELİR'dir). Borç Depozito / Alacak Gelir
/// (SourceType=DepozitoIrat) + DepozitoIrat iz kaydı TEK transaction; kira bağıyla karne/Karlilik
/// araç atfı. BAĞIMSIZ ORACLE (elle): al 500 → irat 200 → tutulan 300, Gelir 200; kirali irat araca;
/// kirasız → Atanmamış; guard: irat tutulanı aşamaz; kira başka carinin ise red; çift-submit sessiz
/// idempotent (Depozito% kısmi unique index — I3 sözleşmesi); EUR irat 1.1 kur sözleşmesi.
/// </summary>
[Collection("postgres")]
public sealed class DepozitoIratTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private static Task<Guid> CustomerAsync(IServiceProvider sp, string name = "Depo") =>
        sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Cari" });

    [Fact]
    public async Task Irat_gelire_yazar_bakiye_duser_ve_guard()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = await CustomerAsync(sp);
        var store = sp.GetRequiredService<DepositService>();
        var rs = sp.GetRequiredService<ReportService>();

        await store.GetAsync(account, 500m, LedgerAccountType.Kasa);
        await store.ForfeitAsync(account, 200m);                       // Borç Depozito / Alacak Gelir

        Assert.Equal(300m, await store.GetBalanceAsync(account));    // 500 − 200 (elle)
        var gg = await rs.GetRevenueExpenseAsync();
        Assert.Equal(200m, gg.GelirToplam);                     // irat = gelir
        Assert.Equal(0m, gg.GiderToplam);

        // Guard: kalan 300 iken 301 irat → red, hiçbir şey değişmez.
        await Assert.ThrowsAsync<ValidationException>(() => store.ForfeitAsync(account, 301m));
        Assert.Equal(300m, await store.GetBalanceAsync(account));
        Assert.Equal(200m, (await rs.GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Kirali_irat_araca_atfedilir_karne_ve_karlilik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = await CustomerAsync(sp);
        var vehicle = await sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>()
            .CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 IR 01" });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m });
        var store = sp.GetRequiredService<DepositService>();
        await store.GetAsync(account, 500m, LedgerAccountType.Kasa);

        await store.ForfeitAsync(account, 150m, rentalId: rental, description: "Hasar kesintisi");

        var rs = sp.GetRequiredService<ReportService>();
        // Karlilik: tek satır, araca atfedildi (Atanmamış YOK).
        var k = await rs.GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Equal(vehicle, row.VehicleId);
        Assert.Equal(150m, row.Gelir);
        // Karne: kaynak "Depozito İradı" + olay çizelgesinde DeftereYansir=true; parite korunur.
        var scorecard = await rs.GetVehicleScorecardAsync(vehicle);
        var source = Assert.Single(scorecard!.GelirKaynak);
        Assert.Equal("Depozito İradı", source.Kategori);
        Assert.Equal(150m, source.Tutar);
        Assert.Contains(scorecard.Olaylar, o => o.Tur == "Depozito İradı" && o.Tutar == 150m && o.DeftereYansir);
        Assert.Equal(row.Gelir, scorecard.ToplamGelir);           // karne == Karlilik (drift kilidi)
    }

    [Fact]
    public async Task Kirasiz_irat_atanmamista()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = await CustomerAsync(sp);
        var store = sp.GetRequiredService<DepositService>();
        await store.GetAsync(account, 100m, LedgerAccountType.Kasa);

        await store.ForfeitAsync(account, 80m);                        // kira bağı yok

        var k = await sp.GetRequiredService<ReportService>().GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Null(row.VehicleId);                           // (Atanmamış) — görünür, kayıp değil
        Assert.Equal(80m, row.Gelir);
    }

    [Fact]
    public async Task Baska_carinin_kirasina_irat_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var accountA = await CustomerAsync(sp, "A");
        var accountB = await CustomerAsync(sp, "B");
        var vehicle = await sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>()
            .CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 IR 02" });
        var rentalB = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = accountB, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m });
        var store = sp.GetRequiredService<DepositService>();
        await store.GetAsync(accountA, 500m, LedgerAccountType.Kasa);

        // A'nın deposu B'nin kirasına atfedilemez (yanlış araca gelir çiti) — atomik red.
        await Assert.ThrowsAsync<ValidationException>(
            () => store.ForfeitAsync(accountA, 100m, rentalId: rentalB));
        Assert.Equal(500m, await store.GetBalanceAsync(accountA));   // değişmedi
        Assert.Equal(0m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Cift_submit_sessiz_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = await CustomerAsync(sp);
        var store = sp.GetRequiredService<DepositService>();
        await store.GetAsync(account, 500m, LedgerAccountType.Kasa);

        var key = Guid.NewGuid();
        await store.ForfeitAsync(account, 200m, operationKey: key);
        await store.ForfeitAsync(account, 200m, operationKey: key); // çift-submit → sessiz no-op (I3)

        Assert.Equal(300m, await store.GetBalanceAsync(account));    // BİR kez düştü (500−200)
        Assert.Equal(200m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Eszamanli_iratlar_tutulani_asamaz() // adversarial 1.2 Medium (TOCTOU) düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account;
        using (var seed = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(seed.ServiceProvider);
            await seed.ServiceProvider.GetRequiredService<DepositService>()
                .GetAsync(account, 500m, LedgerAccountType.Kasa);
        }

        // 500 tutulan; eşzamanlı 2×300 irat — eski pre-check ikisini de geçiriyordu (bakiye −100).
        var success = 0;
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try
            {
                await s.ServiceProvider.GetRequiredService<DepositService>().ForfeitAsync(account, 300m);
                Interlocked.Increment(ref success);
            }
            catch (ValidationException) { /* kilit arkasındaki bakiye kontrolü reddeder */ }
        })));

        using var check = host.ScopeFor(tenant);
        var sp = check.ServiceProvider;
        Assert.Equal(1, success);                                                       // tam BİR başarı
        Assert.Equal(200m, await sp.GetRequiredService<DepositService>().GetBalanceAsync(account)); // 500−300, NEGATİF DEĞİL
        Assert.Equal(300m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Eszamanli_iadeler_tutulani_asamaz() // aynı sınıf — İade de kilitli yola alındı
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account;
        using (var seed = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(seed.ServiceProvider);
            await seed.ServiceProvider.GetRequiredService<DepositService>()
                .GetAsync(account, 500m, LedgerAccountType.Kasa);
        }

        var success = 0;
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try
            {
                await s.ServiceProvider.GetRequiredService<DepositService>()
                    .RefundAsync(account, 300m, LedgerAccountType.Kasa);
                Interlocked.Increment(ref success);
            }
            catch (ValidationException) { }
        })));

        using var check = host.ScopeFor(tenant);
        Assert.Equal(1, success);
        Assert.Equal(200m, await check.ServiceProvider.GetRequiredService<DepositService>().GetBalanceAsync(account));
    }

    [Fact]
    public async Task Capraz_tenant_anahtar_cakismasi_sessiz_yutulmaz() // adversarial 1.2 Low düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        var key = Guid.NewGuid();

        var tenantA = Guid.NewGuid();
        using (var a = host.ScopeFor(tenantA))
        {
            var spA = a.ServiceProvider;
            var accountA = await CustomerAsync(spA, "A");
            await spA.GetRequiredService<DepositService>().GetAsync(accountA, 500m, LedgerAccountType.Kasa);
            await spA.GetRequiredService<DepositService>().ForfeitAsync(accountA, 200m, operationKey: key);
        }

        // Tenant B AYNI GUID'i kullanır (PK global çakışır) → sessiz yutmak B'nin gelirini kaybettirirdi;
        // artık NET RED (tenant-görünür teyit: B kendi tenant'ında kaydı GÖREMEZ → gerçek çift-submit değil).
        using var b = host.ScopeFor(Guid.NewGuid());
        var spB = b.ServiceProvider;
        var accountB = await CustomerAsync(spB, "B");
        await spB.GetRequiredService<DepositService>().GetAsync(accountB, 500m, LedgerAccountType.Kasa);

        await Assert.ThrowsAsync<ValidationException>(
            () => spB.GetRequiredService<DepositService>().ForfeitAsync(accountB, 200m, operationKey: key));
        Assert.Equal(500m, await spB.GetRequiredService<DepositService>().GetBalanceAsync(accountB)); // dokunulmadı
        Assert.Equal(0m, (await spB.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Eur_irat_kur_sozlesmesi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var account = await CustomerAsync(sp);
        var store = sp.GetRequiredService<DepositService>();
        await store.GetAsync(account, 100m, LedgerAccountType.Kasa, currency: "EUR");   // 4000 base tutulan

        await store.ForfeitAsync(account, 50m, currency: "EUR");                          // oto 40 → 2000 base

        Assert.Equal(2000m, await store.GetBalanceAsync(account));                    // 4000 − 2000 (elle)
        Assert.Equal(2000m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);

        // Kur'suz döviz → red (1.1 sözleşmesi), sıfır yan etki.
        await Assert.ThrowsAsync<ValidationException>(() => store.ForfeitAsync(account, 1m, currency: "DKK"));
        Assert.Equal(2000m, await store.GetBalanceAsync(account));
    }
}
