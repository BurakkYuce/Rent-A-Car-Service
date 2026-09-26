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
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private static Task<Guid> CariAsync(IServiceProvider sp, string ad = "Depo") =>
        sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = ad, Soyad = "Cari" });

    [Fact]
    public async Task Irat_gelire_yazar_bakiye_duser_ve_guard()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var depo = sp.GetRequiredService<DepositService>();
        var rs = sp.GetRequiredService<ReportService>();

        await depo.GetAsync(cari, 500m, LedgerAccountType.Kasa);
        await depo.ForfeitAsync(cari, 200m);                       // Borç Depozito / Alacak Gelir

        Assert.Equal(300m, await depo.GetBalanceAsync(cari));    // 500 − 200 (elle)
        var gg = await rs.GetRevenueExpenseAsync();
        Assert.Equal(200m, gg.GelirToplam);                     // irat = gelir
        Assert.Equal(0m, gg.GiderToplam);

        // Guard: kalan 300 iken 301 irat → red, hiçbir şey değişmez.
        await Assert.ThrowsAsync<ValidationException>(() => depo.ForfeitAsync(cari, 301m));
        Assert.Equal(300m, await depo.GetBalanceAsync(cari));
        Assert.Equal(200m, (await rs.GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Kirali_irat_araca_atfedilir_karne_ve_karlilik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var vehicle = await sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>()
            .CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 IR 01" });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m });
        var depo = sp.GetRequiredService<DepositService>();
        await depo.GetAsync(cari, 500m, LedgerAccountType.Kasa);

        await depo.ForfeitAsync(cari, 150m, rentalId: rental, description: "Hasar kesintisi");

        var rs = sp.GetRequiredService<ReportService>();
        // Karlilik: tek satır, araca atfedildi (Atanmamış YOK).
        var k = await rs.GetProfitabilityAsync();
        var satir = Assert.Single(k.Satirlar);
        Assert.Equal(vehicle, satir.VehicleId);
        Assert.Equal(150m, satir.Gelir);
        // Karne: kaynak "Depozito İradı" + olay çizelgesinde DeftereYansir=true; parite korunur.
        var karne = await rs.GetVehicleScorecardAsync(vehicle);
        var kaynak = Assert.Single(karne!.GelirKaynak);
        Assert.Equal("Depozito İradı", kaynak.Kategori);
        Assert.Equal(150m, kaynak.Tutar);
        Assert.Contains(karne.Olaylar, o => o.Tur == "Depozito İradı" && o.Tutar == 150m && o.DeftereYansir);
        Assert.Equal(satir.Gelir, karne.ToplamGelir);           // karne == Karlilik (drift kilidi)
    }

    [Fact]
    public async Task Kirasiz_irat_atanmamista()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var depo = sp.GetRequiredService<DepositService>();
        await depo.GetAsync(cari, 100m, LedgerAccountType.Kasa);

        await depo.ForfeitAsync(cari, 80m);                        // kira bağı yok

        var k = await sp.GetRequiredService<ReportService>().GetProfitabilityAsync();
        var satir = Assert.Single(k.Satirlar);
        Assert.Null(satir.VehicleId);                           // (Atanmamış) — görünür, kayıp değil
        Assert.Equal(80m, satir.Gelir);
    }

    [Fact]
    public async Task Baska_carinin_kirasina_irat_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariA = await CariAsync(sp, "A");
        var cariB = await CariAsync(sp, "B");
        var vehicle = await sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>()
            .CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 IR 02" });
        var rentalB = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cariB, VehicleId = vehicle, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
        var depo = sp.GetRequiredService<DepositService>();
        await depo.GetAsync(cariA, 500m, LedgerAccountType.Kasa);

        // A'nın deposu B'nin kirasına atfedilemez (yanlış araca gelir çiti) — atomik red.
        await Assert.ThrowsAsync<ValidationException>(
            () => depo.ForfeitAsync(cariA, 100m, rentalId: rentalB));
        Assert.Equal(500m, await depo.GetBalanceAsync(cariA));   // değişmedi
        Assert.Equal(0m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Cift_submit_sessiz_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var depo = sp.GetRequiredService<DepositService>();
        await depo.GetAsync(cari, 500m, LedgerAccountType.Kasa);

        var anahtar = Guid.NewGuid();
        await depo.ForfeitAsync(cari, 200m, operationKey: anahtar);
        await depo.ForfeitAsync(cari, 200m, operationKey: anahtar); // çift-submit → sessiz no-op (I3)

        Assert.Equal(300m, await depo.GetBalanceAsync(cari));    // BİR kez düştü (500−200)
        Assert.Equal(200m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Eszamanli_iratlar_tutulani_asamaz() // adversarial 1.2 Medium (TOCTOU) düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid cari;
        using (var seed = host.ScopeFor(tenant))
        {
            cari = await CariAsync(seed.ServiceProvider);
            await seed.ServiceProvider.GetRequiredService<DepositService>()
                .GetAsync(cari, 500m, LedgerAccountType.Kasa);
        }

        // 500 tutulan; eşzamanlı 2×300 irat — eski pre-check ikisini de geçiriyordu (bakiye −100).
        var basari = 0;
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try
            {
                await s.ServiceProvider.GetRequiredService<DepositService>().ForfeitAsync(cari, 300m);
                Interlocked.Increment(ref basari);
            }
            catch (ValidationException) { /* kilit arkasındaki bakiye kontrolü reddeder */ }
        })));

        using var check = host.ScopeFor(tenant);
        var sp = check.ServiceProvider;
        Assert.Equal(1, basari);                                                       // tam BİR başarı
        Assert.Equal(200m, await sp.GetRequiredService<DepositService>().GetBalanceAsync(cari)); // 500−300, NEGATİF DEĞİL
        Assert.Equal(300m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Eszamanli_iadeler_tutulani_asamaz() // aynı sınıf — İade de kilitli yola alındı
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid cari;
        using (var seed = host.ScopeFor(tenant))
        {
            cari = await CariAsync(seed.ServiceProvider);
            await seed.ServiceProvider.GetRequiredService<DepositService>()
                .GetAsync(cari, 500m, LedgerAccountType.Kasa);
        }

        var basari = 0;
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try
            {
                await s.ServiceProvider.GetRequiredService<DepositService>()
                    .RefundAsync(cari, 300m, LedgerAccountType.Kasa);
                Interlocked.Increment(ref basari);
            }
            catch (ValidationException) { }
        })));

        using var check = host.ScopeFor(tenant);
        Assert.Equal(1, basari);
        Assert.Equal(200m, await check.ServiceProvider.GetRequiredService<DepositService>().GetBalanceAsync(cari));
    }

    [Fact]
    public async Task Capraz_tenant_anahtar_cakismasi_sessiz_yutulmaz() // adversarial 1.2 Low düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        var anahtar = Guid.NewGuid();

        var tenantA = Guid.NewGuid();
        using (var a = host.ScopeFor(tenantA))
        {
            var spA = a.ServiceProvider;
            var cariA = await CariAsync(spA, "A");
            await spA.GetRequiredService<DepositService>().GetAsync(cariA, 500m, LedgerAccountType.Kasa);
            await spA.GetRequiredService<DepositService>().ForfeitAsync(cariA, 200m, operationKey: anahtar);
        }

        // Tenant B AYNI GUID'i kullanır (PK global çakışır) → sessiz yutmak B'nin gelirini kaybettirirdi;
        // artık NET RED (tenant-görünür teyit: B kendi tenant'ında kaydı GÖREMEZ → gerçek çift-submit değil).
        using var b = host.ScopeFor(Guid.NewGuid());
        var spB = b.ServiceProvider;
        var cariB = await CariAsync(spB, "B");
        await spB.GetRequiredService<DepositService>().GetAsync(cariB, 500m, LedgerAccountType.Kasa);

        await Assert.ThrowsAsync<ValidationException>(
            () => spB.GetRequiredService<DepositService>().ForfeitAsync(cariB, 200m, operationKey: anahtar));
        Assert.Equal(500m, await spB.GetRequiredService<DepositService>().GetBalanceAsync(cariB)); // dokunulmadı
        Assert.Equal(0m, (await spB.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);
    }

    [Fact]
    public async Task Eur_irat_kur_sozlesmesi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var cari = await CariAsync(sp);
        var depo = sp.GetRequiredService<DepositService>();
        await depo.GetAsync(cari, 100m, LedgerAccountType.Kasa, currency: "EUR");   // 4000 base tutulan

        await depo.ForfeitAsync(cari, 50m, currency: "EUR");                          // oto 40 → 2000 base

        Assert.Equal(2000m, await depo.GetBalanceAsync(cari));                    // 4000 − 2000 (elle)
        Assert.Equal(2000m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GelirToplam);

        // Kur'suz döviz → red (1.1 sözleşmesi), sıfır yan etki.
        await Assert.ThrowsAsync<ValidationException>(() => depo.ForfeitAsync(cari, 1m, currency: "DKK"));
        Assert.Equal(2000m, await depo.GetBalanceAsync(cari));
    }
}
