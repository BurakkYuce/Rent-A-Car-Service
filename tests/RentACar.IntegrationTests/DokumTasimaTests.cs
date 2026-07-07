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
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Grup = "B" });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Dok", Soyad = "M" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        { Kod = "DK-B", Ad = "DK", AracGrupKod = "B", ParaBirimi = "TRY", Gun1 = 300m, Gun2 = 280m, Gun3 = 240m, OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "t" });
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

        var rezId = await res.CreateAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), FiyatTuru = "Otomatik" });

        // Rezervasyonda döküm dolu (Tutar 432, hediye 1, iskonto 48).
        var rez = await res.GetAsync(rezId);
        Assert.Equal(432m, rez!.Tutar);
        Assert.Equal(1, rez.HediyeGun);
        Assert.Equal(48m, rez.IskontoTutar);

        // Rez → Kira dönüşümü: döküm de taşınır (PR4b L2 boşluğu kapandı).
        var kiraId = await res.ConvertToRentalAsync(rezId);
        var s = await sp.GetRequiredService<SozlesmeService>().GetAsync(kiraId);
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
        var teklifId = await sp.GetRequiredService<QuotationService>().CreateAsync(new QuotationInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), FiyatTuru = "Otomatik" });
        var rezId = await sp.GetRequiredService<QuotationService>().AcceptAsync(teklifId);

        var rez = await sp.GetRequiredService<ReservationService>().GetAsync(rezId);
        Assert.Equal(432m, rez!.Tutar);
        Assert.Equal(1, rez.HediyeGun);      // teklif → rez döküm taşındı
        Assert.Equal(48m, rez.IskontoTutar);
    }
}
