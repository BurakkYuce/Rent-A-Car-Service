using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap H2 — rezervasyon kaynak + fatura dönem raporları. BAĞIMSIZ ORACLE: 2 "Web" rezervasyonu (3 gün × 100)
/// → adet 2 / gün 6 / ciro 600; manuel fatura (net 1000 + kdv 200) → fatura dönem satırı genel toplam 1200.
/// </summary>
[Collection("postgres")]
public sealed class RaporEksik2Tests(PostgresFixture fx)
{
    [Fact]
    public async Task Rezervasyon_kaynak_agrega()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var custId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rez Müşteri" });
        var vehicles = sp.GetRequiredService<VehicleService>();
        var rez = sp.GetRequiredService<ReservationService>();
        var bas = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

        foreach (var plaka in new[] { "34 RZ 01", "34 RZ 02" })
        {
            var vId = await vehicles.CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
            await rez.CreateAsync(new BookingInput
            {
                MusteriId = custId, VehicleId = vId, BasTar = bas, BitTar = bas.AddDays(3),
                GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Web"
            });
        }

        var rows = await sp.GetRequiredService<ReportService>().GetRezervasyonKaynakAsync();
        var web = Assert.Single(rows, r => r.Kaynak == "Web");
        Assert.Equal(2, web.Adet);
        Assert.Equal(6, web.ToplamGun);     // 2 × 3 gün
        Assert.Equal(600m, web.ToplamCiro); // 2 × (3 × 100)
    }

    [Fact]
    public async Task Fatura_donem_listesi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Fatura Müşteri" });
        await sp.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 1000m, KdvOrani = 0.20m });

        var rows = await sp.GetRequiredService<ReportService>().GetFaturaDonemAsync();
        var r = Assert.Single(rows);
        Assert.Equal(1200m, r.GenelToplam);   // 1000 + 200 (elle oracle)
        Assert.Equal("Fatura Müşteri", r.Cari);
        Assert.False(r.IadeMi);
    }
}
