using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap B2 — Araç-bazlı kârlılık (DEFTERDEN). BAĞIMSIZ ORACLE: gelir fatura→kira→araç, gider araç-gideri
/// ile atfedilir; toplamlar gelir-gider defter toplamıyla MUTABIK (invariant); filtre + "(Atanmamış)".
/// </summary>
[Collection("postgres")]
public sealed class KarlilikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Vehicle_pnl_from_ledger_and_reconciles_to_gelirgider()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 KAR 01", Grup = "EKO", Sube = "Merkez" });

        // Gider: araç gideri net 1000 (KDV ayrı hesaba). Gider defteri Debit 1000, AccountRef=araç.
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = vehicleId, NetTutar = 1000m, KdvOrani = 0.20m,
            Doviz = "TRY", Kur = 1m, OdemeYontemi = PaymentMethod.Nakit, Aciklama = "Bakım"
        });

        // Gelir: araca kira + fatura. 4 gün × 100 = 400 brüt → net 333.33 (KDV 66.67).
        var rentalId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = await TestCari.YeniAsync(sp), VehicleId = vehicleId, BasTar = Bas, BitTar = Bas.AddDays(4), GunlukUcret = 100m });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetProfitabilityAsync();

        var row = Assert.Single(k.Satirlar);
        Assert.Equal(vehicleId, row.VehicleId);
        Assert.Equal("34KAR01", row.Plaka);   // plaka boşluksuz normalize edilir
        Assert.Equal(333.33m, row.Gelir);     // fatura net (oracle: 400/1.20)
        Assert.Equal(1000m, row.Gider);        // araç gideri net
        Assert.Equal(-666.67m, row.NetKar);    // 333.33 − 1000

        // INVARIANT: kârlılık toplamları gelir-gider defter toplamlarıyla mutabık.
        var gg = await rs.GetRevenueExpenseAsync();
        Assert.Equal(gg.GelirToplam, k.ToplamGelir);
        Assert.Equal(gg.GiderToplam, k.ToplamGider);
        Assert.Equal(333.33m, k.ToplamGelir);
        Assert.Equal(1000m, k.ToplamGider);
    }

    [Fact]
    public async Task Vehicle_sale_income_attributed_to_vehicle() // adversarial HIGH düzeltmesi
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 SAT 01", Grup = "EKO", Sube = "Merkez" });

        // Araç satışı: net 5000 gelir. SourceType=AracSatis → araca atfedilmeli (Atanmamış'a DEĞİL).
        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        { VehicleId = vehicleId, AliciCariId = Guid.NewGuid(), SatisNet = 5000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m });

        var k = await sp.GetRequiredService<ReportService>().GetProfitabilityAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Equal(vehicleId, row.VehicleId);    // satış geliri araca atfedildi
        Assert.Equal(5000m, row.Gelir);
        Assert.DoesNotContain(k.Satirlar, r => r.VehicleId == null); // Atanmamış YOK
    }

    [Fact]
    public async Task Unattributed_expense_goes_to_atanmamis_and_filter_excludes_it()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 KAR 02", Grup = "EKO", Sube = "Merkez" });

        // Araç gideri 1000 (atfedilir) + genel gider 500 (araçsız → Atanmamış).
        var exp = sp.GetRequiredService<ExpenseService>();
        await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Arac, VehicleId = vehicleId, NetTutar = 1000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = PaymentMethod.Nakit });
        await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 500m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = PaymentMethod.Nakit });

        var rs = sp.GetRequiredService<ReportService>();

        // Filtresiz: araç + Atanmamış (toplam gider 1500, defterle mutabık).
        var hepsi = await rs.GetProfitabilityAsync();
        Assert.Equal(2, hepsi.Satirlar.Count);
        Assert.Contains(hepsi.Satirlar, r => r.VehicleId == null && r.Gider == 500m);
        Assert.Equal(1500m, hepsi.ToplamGider);
        Assert.Equal((await rs.GetRevenueExpenseAsync()).GiderToplam, hepsi.ToplamGider);

        // Şube filtresi: yalnız araç satırı (Atanmamış hariç).
        var filtre = await rs.GetProfitabilityAsync(branch: "Merkez");
        var row = Assert.Single(filtre.Satirlar);
        Assert.Equal(vehicleId, row.VehicleId);
        Assert.Equal(1000m, filtre.ToplamGider);
    }

    [Fact]
    public async Task Karlilik_ozet_boyuta_gore_toplar() // #2 çok-boyutlu P&L — bağımsız oracle
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var exp = sp.GetRequiredService<ExpenseService>();
        var sale = sp.GetRequiredService<VehicleSaleService>();

        // Senaryo (elle kurulmuş oracle; KDV 0 → net = tutar):
        //  v1: grup EKO, segment Ekonomik, şube Merkez → gider 1000, satış geliri 5000 (net 4000)
        //  v2: grup EKO, segment Ekonomik, şube Merkez → gider  500, satış geliri 2000 (net 1500)
        //  v3: grup LUX, segment Lüks,     şube Sube2  → gider  200, satış geliri 1000 (net  800)
        async Task Seed(string plaka, string grup, string segment, string sube, decimal gider, decimal gelir)
        {
            var id = await veh.CreateAsync(new VehicleInput { Plaka = plaka, Grup = grup, Segment = segment, Sube = sube });
            await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Arac, VehicleId = id, NetTutar = gider, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = PaymentMethod.Nakit });
            await sale.CreateAsync(new VehicleSaleInput { VehicleId = id, AliciCariId = Guid.NewGuid(), SatisNet = gelir, KdvOrani = 0m, Doviz = "TRY", Kur = 1m });
        }
        await Seed("34 OZ 01", "EKO", "Ekonomik", "Merkez", 1000m, 5000m);
        await Seed("34 OZ 02", "EKO", "Ekonomik", "Merkez", 500m, 2000m);
        await Seed("34 OZ 03", "LUX", "Lüks", "Sube2", 200m, 1000m);

        var rs = sp.GetRequiredService<ReportService>();

        // GRUP: EKO {2 araç, gelir 7000, gider 1500, net 5500} > LUX {1, 1000, 200, 800} (net desc sıra).
        var grup = await rs.GetProfitabilitySummaryAsync("grup");
        Assert.Equal("Grup", grup.BoyutAdi);
        Assert.Equal(2, grup.Satirlar.Count);
        var eko = grup.Satirlar[0];
        Assert.Equal("EKO", eko.Boyut);
        Assert.Equal(2, eko.AracAdet);
        Assert.Equal(7000m, eko.Gelir);
        Assert.Equal(1500m, eko.Gider);
        Assert.Equal(5500m, eko.NetKar);
        Assert.Equal("LUX", grup.Satirlar[1].Boyut);
        Assert.Equal(800m, grup.Satirlar[1].NetKar);
        Assert.Equal(8000m, grup.ToplamGelir);
        Assert.Equal(1700m, grup.ToplamGider);
        Assert.Equal(6300m, grup.ToplamNetKar);

        // SEGMENT: Ekonomik {2, 7000, 1500, 5500}, Lüks {1, 1000, 200, 800}.
        var seg = await rs.GetProfitabilitySummaryAsync("segment");
        Assert.Equal("Segment", seg.BoyutAdi);
        var ekonomik = Assert.Single(seg.Satirlar, s => s.Boyut == "Ekonomik");
        Assert.Equal(2, ekonomik.AracAdet);
        Assert.Equal(5500m, ekonomik.NetKar);

        // ŞUBE: Merkez {2, net 5500}, Sube2 {1, net 800}.
        var subeOzet = await rs.GetProfitabilitySummaryAsync("sube");
        Assert.Equal("Şube", subeOzet.BoyutAdi);
        var merkez = Assert.Single(subeOzet.Satirlar, s => s.Boyut == "Merkez");
        Assert.Equal(2, merkez.AracAdet);
        Assert.Equal(5500m, merkez.NetKar);
        Assert.Equal(2, subeOzet.Satirlar.Count);

        // INVARIANT: özet toplamı = araç-bazlı karlılık toplamı (tümü araca atfedildiğinden Atanmamış yok).
        var arac = await rs.GetProfitabilityAsync();
        Assert.Equal(arac.ToplamNetKar, grup.ToplamNetKar);
    }
}
