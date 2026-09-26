using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Penalties;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç-karne PR1 — kârlılık ATIF sağlamlaştırma. Önceden "(Atanmamış)"a düşen gerçek araç gelirleri artık
/// araca atfedilir: (1) FARK faturası (RentalId=null, kira bağı KaynakKiraId'de), (2) iade-of-fark
/// (KaynakFaturaId→fark→KaynakKiraId iki-hop), (3) ServisYansitma (ServiceRecord.VehicleId),
/// (4) Ceza VehicleId=null iken RentalId→kira→araç fallback.
/// BAĞIMSIZ ORACLE (elle): 3g×100=300 brüt→net 250 (KDV %20); fark 600 brüt→net 500; servis 1000×0.5=500;
/// ceza 100. İNVARYANT: atıf düzeltmesi toplam Geliri DEĞİŞTİRMEZ (defterle mutabık), yalnız dağıtım düzelir.
/// </summary>
[Collection("postgres")]
public sealed class KarlilikAtifTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    /// <summary>3 gün × 100 = 300 brüt baz; KmLimit 300, FazlaKmUcret 2 → dönüşte 300 aşım = 600 fark.</summary>
    private static async Task<(Guid rental, Guid vehicle, Guid cari)> KiraKurAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Atif", Soyad = "Musteri" });
        var r = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 2m
        });
        return (r, v, m);
    }

    /// <summary>Base fatura + dönüş (300 aşım × 2 = 600) + FARK faturası; fark fatura id döner.</summary>
    private static async Task<Guid> FarkSenaryosuAsync(IServiceProvider sp, Guid rental)
    {
        var invoices = sp.GetRequiredService<InvoiceService>();
        var rentals = sp.GetRequiredService<RentalService>();
        await invoices.CreateFromRentalAsync(rental);                                     // base 300 brüt → net 250
        await rentals.DeliverAsync(rental, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(rental, returnKm: 1600, returnFuel: 8, Bas.AddDays(3));  // sözleşme 900
        return await invoices.CreateFromRentalAsync(rental);                              // FARK 600 brüt → net 500
    }

    [Fact]
    public async Task Fark_faturasi_geliri_araca_atfedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, vehicle, _) = await KiraKurAsync(sp, "34 AT 01");
        await FarkSenaryosuAsync(sp, rental);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();

        var row = Assert.Single(k.Satirlar);          // "(Atanmamış)" satırı YOK — fark araca gitti
        Assert.Equal(vehicle, row.VehicleId);
        Assert.Equal(750m, row.Gelir);                // 250 (base) + 500 (fark) — elle
        // İNVARYANT: atıf toplamı değiştirmez — defter Gelir toplamıyla mutabık.
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
        Assert.Equal(750m, k.ToplamGelir);
    }

    [Fact]
    public async Task Fark_iadesi_ayni_araca_negatif_netlesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, vehicle, _) = await KiraKurAsync(sp, "34 AT 02");
        var farkId = await FarkSenaryosuAsync(sp, rental);

        // Fark faturasının KENDİSİ iade edilir → iade'nin KaynakFaturaId'si fark'a işaret eder;
        // fark'ın RentalId'si null olduğundan kira bağı ancak KaynakKiraId iki-hop'uyla çözülür.
        await sp.GetRequiredService<InvoiceService>().CreateRefundAsync(farkId);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);          // iade Atanmamış'a düşmedi, aynı araçta netleşti
        Assert.Equal(vehicle, row.VehicleId);
        Assert.Equal(250m, row.Gelir);                // 250 + 500 − 500 — elle
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
    }

    [Fact]
    public async Task Servis_yansitma_geliri_araca_atfedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 AT 03" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Rucu", Soyad = "Cari" });

        // Servis maliyeti 1000, kusur 0.5 → rücu 500 (elle). SourceType=ServisYansitma.
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var svcId = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = vehicle, Tip = ServiceType.Ariza, GirisKm = 0,
            HasarSorumlu = DamageResponsible.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Tampon", Tutar = 1000m }]
        });
        await svc.StartAsync(svcId);
        await svc.CompleteAsync(svcId, pickupKm: 100);
        await svc.ReflectAsync(svcId, cari);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);          // Atanmamış YOK — rücu geliri servis aracına
        Assert.Equal(vehicle, row.VehicleId);
        Assert.Equal(500m, row.Gelir);
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
    }

    [Fact]
    public async Task Ceza_araci_yoksa_kiradan_atfedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, vehicle, cari) = await KiraKurAsync(sp, "34 AT 04");

        // Ceza VehicleId=NULL ama RentalId dolu → kira → araç fallback. Tutar 100 (elle).
        var pen = sp.GetRequiredService<PenaltyService>();
        var pid = await pen.CreateAsync(new PenaltyInput
        { CezaTuru = "Hız", VehicleId = null, RentalId = rental, CariId = cari, Tutar = 100m });
        await pen.ReflectAsync(pid);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Equal(vehicle, row.VehicleId);
        Assert.Equal(100m, row.Gelir);                // yalnız ceza yansıtması (fatura kesilmedi)
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GelirToplam, k.ToplamGelir);
    }

    [Fact]
    public async Task Arac_ve_kirasiz_ceza_atanmamista_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Serbest", Soyad = "Cari" });

        // Ne araç ne kira bağı → atfedilemez; doğru davranış "(Atanmamış)" (kayıp değil, görünür).
        var pen = sp.GetRequiredService<PenaltyService>();
        var pid = await pen.CreateAsync(new PenaltyInput { CezaTuru = "Park", CariId = cari, Tutar = 80m });
        await pen.ReflectAsync(pid);

        var k = await sp.GetRequiredService<ReportService>().GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Null(row.VehicleId);
        Assert.Equal(80m, row.Gelir);                 // toplam yine defterle mutabık
    }

    [Fact]
    public async Task Capraz_tenant_atif_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        using (var scopeA = host.ScopeFor(tenantA))
        {
            var (rental, _, _) = await KiraKurAsync(scopeA.ServiceProvider, "34 AT 05");
            await FarkSenaryosuAsync(scopeA.ServiceProvider, rental);
        }

        // Tenant B (racar_app + RLS): A'nın fark geliri görünmez.
        using var scopeB = host.ScopeFor(Guid.NewGuid());
        var k = await scopeB.ServiceProvider.GetRequiredService<ReportService>().GetProfitabilityAsync();
        Assert.Empty(k.Satirlar);
    }
}
