using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulguları a-kkayit-09 ve C-GUN (PARA): geç dönüşte tolerans yoktu — 0,48 sn gecikme bile 3 günlük kirayı
/// 4 gün faturalatıyordu; "Faturasız kiralar" listesi 3.600 gösterirken kesilen fatura 4.800 oluyordu.
///
/// ELLE ORACLE: 3 gün × 1.200 (KDV dahil) = 3.600. Kira gün kuralı: kısmi süre 3 saati BULURSA +1 gün.
/// 1 dk geç → 3.600; 4 sa geç → 4 gün = 4.800. Liste tutarı = kesilen fatura brütü (her iki durumda).
/// </summary>
[Collection("postgres")]
public sealed class GecDonusToleransTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(-10);

    private static async Task<(Guid Rental, RentalService Rentals)> DeliveredAsync(IServiceProvider sp)
    {
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = await TestCustomer.NewAsync(sp), VehicleId = await TestVehicle.NewAsync(sp),
            BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1200m,
        });
        await rentals.DeliverAsync(id, pickupKm: 10_000, pickupFuel: 8);
        return (id, rentals);
    }

    [Theory]
    [InlineData(0, 3600)]        // tam zamanında
    [InlineData(1, 3600)]        // 1 dk geç → tolerans içinde
    [InlineData(179, 3600)]      // 2 sa 59 dk geç → tolerans içinde
    [InlineData(240, 4800)]      // 4 sa geç → +1 gün
    public async Task Gec_donus_toleransi_ve_liste_fatura_tutarliligi(int lateMinutes, int expected)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (id, rentals) = await DeliveredAsync(sp);
        var actual = Start.AddDays(3).AddMinutes(lateMinutes);

        var preview = await rentals.PreviewReturnAsync(id, 10_100, 8, actual);
        Assert.Equal((decimal)expected, preview.YeniGenelToplam);

        await rentals.ReturnAsync(id, 10_100, 8, actual);
        Assert.Equal((decimal)expected, (await rentals.GetAsync(id))!.GenelToplam);

        // "Faturasız kiralar" (toplu faturalama adayları) satırı kesilecek faturanın brütünü gösterir.
        var row = Assert.Single(await rentals.SearchAsync(new RentalFilter { Faturali = false }), r => r.Id == id);
        Assert.Equal((decimal)expected, row.GenelToplam);

        var invoices = sp.GetRequiredService<InvoiceService>();
        var invoice = (await invoices.GetAsync(await invoices.CreateFromRentalAsync(id)))!;
        Assert.Equal(row.GenelToplam, invoice.GenelToplam);
    }
}
