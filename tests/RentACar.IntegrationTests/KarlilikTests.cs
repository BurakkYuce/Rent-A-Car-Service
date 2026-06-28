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
            Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Aciklama = "Bakım"
        });

        // Gelir: araca kira + fatura. 4 gün × 100 = 400 brüt → net 333.33 (KDV 66.67).
        var rentalId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = Guid.NewGuid(), VehicleId = vehicleId, BasTar = Bas, BitTar = Bas.AddDays(4), GunlukUcret = 100m });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetKarlilikAsync();

        var row = Assert.Single(k.Satirlar);
        Assert.Equal(vehicleId, row.VehicleId);
        Assert.Equal("34KAR01", row.Plaka);   // plaka boşluksuz normalize edilir
        Assert.Equal(333.33m, row.Gelir);     // fatura net (oracle: 400/1.20)
        Assert.Equal(1000m, row.Gider);        // araç gideri net
        Assert.Equal(-666.67m, row.NetKar);    // 333.33 − 1000

        // INVARIANT: kârlılık toplamları gelir-gider defter toplamlarıyla mutabık.
        var gg = await rs.GetGelirGiderAsync();
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

        var k = await sp.GetRequiredService<ReportService>().GetKarlilikAsync();
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
        await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Arac, VehicleId = vehicleId, NetTutar = 1000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit });
        await exp.CreateAsync(new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 500m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit });

        var rs = sp.GetRequiredService<ReportService>();

        // Filtresiz: araç + Atanmamış (toplam gider 1500, defterle mutabık).
        var hepsi = await rs.GetKarlilikAsync();
        Assert.Equal(2, hepsi.Satirlar.Count);
        Assert.Contains(hepsi.Satirlar, r => r.VehicleId == null && r.Gider == 500m);
        Assert.Equal(1500m, hepsi.ToplamGider);
        Assert.Equal((await rs.GetGelirGiderAsync()).GiderToplam, hepsi.ToplamGider);

        // Şube filtresi: yalnız araç satırı (Atanmamış hariç).
        var filtre = await rs.GetKarlilikAsync(sube: "Merkez");
        var row = Assert.Single(filtre.Satirlar);
        Assert.Equal(vehicleId, row.VehicleId);
        Assert.Equal(1000m, filtre.ToplamGider);
    }
}
