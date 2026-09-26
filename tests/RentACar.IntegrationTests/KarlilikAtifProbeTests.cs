using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Integrations;
using RentACar.Application.Penalties;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// ADVERSARIAL PROBE — kârlılık atıf değişikliği (fark/iade-of-fark/ServisYansitma/ceza-fallback).
/// Throwaway: commit edilmez. Oracles elle kurulmuş senaryodan.
/// </summary>
[Collection("postgres")]
public sealed class KarlilikAtifProbeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private sealed class FakeHgs(IReadOnlyList<TollCrossing> crossings) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(
            string plate, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(crossings);
    }

    private static async Task<(Guid rental, Guid vehicle, Guid cari)> RentalExchangeRateAsync(IServiceProvider sp, string plate)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Probe", Soyad = "Musteri" });
        var r = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 2m
        });
        return (r, v, m);
    }

    /// <summary>
    /// KITCHEN-SINK invaryant: TÜM gelir kaynak türleri aynı tenantta karışık — atıf toplamı DEĞİŞTİRMEZ,
    /// her satır elle hesaplanan değerde. V1: base 250 + fark 500 − fark-iade 500 + ceza(kira-fallback) 100
    /// + servis rücu 500 = 850. V2: satış 10000. Atanmamış: HGS 103 + manuel 200 − manuel-iade 200
    /// + serbest ceza 80 + SARKIK-RentalId ceza 50 = 233. Toplam 11083.
    /// </summary>
    [Fact]
    public async Task Probe_KitchenSink_toplam_invaryant_ve_satir_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // --- V1: base + fark + fark iadesi ---
        var (rental, v1, account) = await RentalExchangeRateAsync(sp, "34 PR 01");
        var invoices = sp.GetRequiredService<InvoiceService>();
        var rentals = sp.GetRequiredService<RentalService>();
        await invoices.CreateFromRentalAsync(rental);                                     // 300 brüt → net 250
        await rentals.DeliverAsync(rental, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(rental, returnKm: 1600, returnFuel: 8, Start.AddDays(3));  // +600 aşım
        var differenceId = await invoices.CreateFromRentalAsync(rental);                        // fark 600 brüt → net 500
        await invoices.CreateRefundAsync(differenceId);                                           // −500 (iki-hop)

        // --- V1: ceza kira-fallback (100) ---
        var pen = sp.GetRequiredService<PenaltyService>();
        var p1 = await pen.CreateAsync(new PenaltyInput
        { CezaTuru = "Hız", RentalId = rental, CariId = account, Tutar = 100m });
        await pen.ReflectAsync(p1);

        // --- V1: servis rücu 1000 × 0.5 = 500 ---
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var svcId = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = v1, Tip = ServiceType.Ariza, GirisKm = 0,
            HasarSorumlu = DamageResponsible.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Tampon", Tutar = 1000m }]
        });
        await svc.StartAsync(svcId);
        await svc.CompleteAsync(svcId, pickupKm: 100);
        await svc.ReflectAsync(svcId, account);

        // --- V2: araç satışı net 10000 ---
        var v2 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 PR 02" });
        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        { VehicleId = v2, AliciCariId = account, SatisNet = 10000m, KdvOrani = 0.20m, Kur = 1m });

        // --- Atanmamış: HGS 100 × 1.03 = 103 ---
        var hgs = new HgsReflectionService(
            new FakeHgs([new TollCrossing(Start.AddDays(1), "Köprü", 100m)]),
            sp.GetRequiredService<ILedgerPoster>(),
            sp.GetRequiredService<IPeriodLockGuard>(),
            sp.GetRequiredService<ICurrentUser>());
        await hgs.ReflectAsync(account, "34 PR 01", Start, Start.AddDays(3));

        // --- Atanmamış: manuel fatura 200 + iadesi (−200) ---
        var manId = await invoices.CreateManualAsync(new ManualInvoiceInput { CariId = account, NetTutar = 200m });
        await invoices.CreateRefundAsync(manId);

        // --- Atanmamış: serbest ceza 80 (araçsız+kirasız) ---
        var p2 = await pen.CreateAsync(new PenaltyInput { CezaTuru = "Park", CariId = account, Tutar = 80m });
        await pen.ReflectAsync(p2);

        // --- Atanmamış: SARKIK RentalId'li ceza 50 (var olmayan kira — exception atmamalı) ---
        var p3 = await pen.CreateAsync(new PenaltyInput
        { CezaTuru = "Şerit", RentalId = Guid.NewGuid(), CariId = account, Tutar = 50m });
        await pen.ReflectAsync(p3);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();

        // Satır oracles (elle):
        var rowV1 = Assert.Single(k.Satirlar, r => r.VehicleId == v1);
        Assert.Equal(850m, rowV1.Gelir);   // 250 + 500 − 500 + 100 + 500
        var rowV2 = Assert.Single(k.Satirlar, r => r.VehicleId == v2);
        Assert.Equal(10000m, rowV2.Gelir);
        var rowAt = Assert.Single(k.Satirlar, r => r.VehicleId == null);
        Assert.Equal(233m, rowAt.Gelir);   // 103 + 200 − 200 + 80 + 50

        // İNVARYANT: atıf toplamı değiştirmez — defterle mutabık.
        Assert.Equal(11083m, k.ToplamGelir);
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
    }

    /// <summary>Ceza HEM VehicleId HEM (başka araçlı) RentalId taşır → VehicleId kazanmalı; çift sayım YOK.</summary>
    [Fact]
    public async Task Probe_Ceza_VehicleId_ve_RentalId_celiskisinde_VehicleId_kazanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var (rental, vB, account) = await RentalExchangeRateAsync(sp, "34 PR 11");     // kira aracı = vB
        var vA = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 PR 12" });

        var pen = sp.GetRequiredService<PenaltyService>();
        var pid = await pen.CreateAsync(new PenaltyInput
        { CezaTuru = "Hız", VehicleId = vA, RentalId = rental, CariId = account, Tutar = 100m });
        await pen.ReflectAsync(pid);

        var k = await sp.GetRequiredService<ReportService>().GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);           // tek satır — çift sayım/bölünme yok
        Assert.Equal(vA, row.VehicleId);               // açık atama türetilmiş bağı yener
        Assert.Equal(100m, row.Gelir);
        Assert.Equal(100m, k.ToplamGelir);
    }

    /// <summary>İade'nin iadesi (üç-hop gereksinimi doğuracak zincir) API'de ENGELLİ olmalı.</summary>
    [Fact]
    public async Task Probe_Iade_of_iade_engelli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var (rental, _, _) = await RentalExchangeRateAsync(sp, "34 PR 21");
        var invoices = sp.GetRequiredService<InvoiceService>();
        var baseId = await invoices.CreateFromRentalAsync(rental);
        var refundId = await invoices.CreateRefundAsync(baseId);

        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateRefundAsync(refundId));
    }

    /// <summary>FX servis rücu: 1000×0.5=500 EUR, kur 40 → 20.000 TL baz. A×R her yerde.</summary>
    [Fact]
    public async Task Probe_ServisYansitma_FX_baz_tutar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 PR 31" });
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Fx", Soyad = "Cari" });

        var svc = sp.GetRequiredService<ServiceRecordService>();
        var svcId = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = v, Tip = ServiceType.Ariza, GirisKm = 0,
            HasarSorumlu = DamageResponsible.Sigorta, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Kaporta", Tutar = 1000m }]
        });
        await svc.StartAsync(svcId);
        await svc.CompleteAsync(svcId, pickupKm: 10);
        await svc.ReflectAsync(svcId, account, currency: "EUR", exchangeRate: 40m);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Equal(v, row.VehicleId);
        Assert.Equal(20000m, row.Gelir);               // 500 EUR × 40 — elle
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
    }

    /// <summary>Base fatura iade edilip kira YENİDEN faturalanırsa (fark yolu) atıf yine araçta ve net 250.</summary>
    [Fact]
    public async Task Probe_Base_iade_sonrasi_yeniden_fatura_atfi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var (rental, v, _) = await RentalExchangeRateAsync(sp, "34 PR 41");
        var invoices = sp.GetRequiredService<InvoiceService>();
        var baseId = await invoices.CreateFromRentalAsync(rental);   // +250
        await invoices.CreateRefundAsync(baseId);                      // −250
        await invoices.CreateFromRentalAsync(rental);                // yeniden: fark yolu, +250

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Equal(v, row.VehicleId);
        Assert.Equal(250m, row.Gelir);                 // 250 − 250 + 250 — elle
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
    }
}
