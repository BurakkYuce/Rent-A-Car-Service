using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-F2 — tam teklif dökümü (hediye/iskonto/hafta-sonu/faturalanan) Reservation + Quotation'a taşınır ve
/// dönüşümde (Rez→Kira) kiraya aktarılır → dönüşen kiranın sözleşmesinde de döküm görünür (PR4b L2 kapandı).
/// BAĞIMSIZ ORACLE: matris 240/gün, hediye 1 + iskonto %10 → faturalanan 2×240=480−48=432; döküm hediye=1/iskonto=48.
/// </summary>
[Collection("postgres")]
public sealed class DokumTasimaTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plate)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Grup = "B" });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Dok", Soyad = "M" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        { Kod = "DK-B", Ad = "DK", AracGrupKod = "B", ParaBirimi = "TRY", Gun1 = 300m, Gun2 = 280m, Gun3 = 240m, OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t" });
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "DK-R", Ad = "DK", AracGrupKod = "B", HediyeGun = 1, Iskonto = 10m });
        return (m, v);
    }

    [Fact]
    public async Task Otomatik_rezervasyonda_dokum_dolu_ve_kiraya_tasinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var res = sp.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(sp, "34 DK 01");

        var resId = await res.CreateAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), FiyatTuru = "Otomatik" });

        // Rezervasyonda döküm dolu (Tutar 432, hediye 1, iskonto 48).
        var resv = await res.GetAsync(resId);
        Assert.Equal(432m, resv!.Tutar);
        Assert.Equal(1, resv.HediyeGun);
        Assert.Equal(48m, resv.IskontoTutar);

        // Rez → Kira dönüşümü: döküm de taşınır (PR4b L2 boşluğu kapandı).
        var rentalId = await res.ConvertToRentalAsync(resId);
        var s = await sp.GetRequiredService<ContractService>().GetAsync(rentalId);
        Assert.Equal(432m, s!.Tutar);        // para (zaten korunuyordu)
        Assert.Equal(1, s.HediyeGun);        // döküm ARTIK taşınıyor
        Assert.Equal(2, s.FaturalananGun);
        Assert.Equal(48m, s.IskontoTutar);
    }

    [Fact]
    public async Task Teklif_kabul_rezervasyona_dokum_tasir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DK 02");

        // Teklif FiyatTuru=Otomatik ile → döküm dolar; Kabul → Rezervasyona taşınır.
        var quotationId = await sp.GetRequiredService<QuotationService>().CreateAsync(new QuotationInput
        { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), FiyatTuru = "Otomatik" });
        var resId = await sp.GetRequiredService<QuotationService>().AcceptAsync(quotationId);

        var res = await sp.GetRequiredService<ReservationService>().GetAsync(resId);
        Assert.Equal(432m, res!.Tutar);
        Assert.Equal(1, res.HediyeGun);      // teklif → rez döküm taşındı
        Assert.Equal(48m, res.IskontoTutar);
    }
}
