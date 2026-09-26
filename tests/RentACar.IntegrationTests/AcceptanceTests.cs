using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Faz 1 KABUL SENARYOSU (plan Böl. 8): müsaitlik → rezervasyon → kiraya çevir (Tasfiye)
/// → teslim (çıkış KM/yakıt) → dönüş (fazla km/uzatma) → tahsilat → cari bakiye — hepsi
/// tek otomatik test. Gerçek RLS'li racar_app bağlantısı + audit + boşluksuz no + çift
/// taraflı defter dahil uçtan uca.
/// </summary>
[Collection("postgres")]
public sealed class AcceptanceTests(PostgresFixture fx)
{
    [Fact]
    public async Task Full_lifecycle_reservation_to_settlement()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "operator");
        var reservations = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        var account = await TestCustomer.NewAsync(scope.ServiceProvider);
        var vehicle = await TestVehicle.NewAsync(scope.ServiceProvider);
        // Göreli: rezervasyon geçmişe kapalı (TarihPolitikasi); sabit 2026-11-01 Kasım'da kırmızıya dönecekti.
        var start = TestZaman.DaysLater(40);
        var bit = start.AddDays(4); // 4 gün

        BookingInput Input() => new()
        {
            MusteriId = account, VehicleId = vehicle, BasTar = start, BitTar = bit,
            GunlukUcret = 100m, KmLimit = 400, FazlaKmUcret = 2m
        };

        // 1) Rezervasyon (RZ-000001, Rezerv)
        var resId = await reservations.CreateAsync(Input());
        var res = await reservations.GetAsync(resId);
        DocumentNoOracle.OneOfExpected(2, 1, res!.ReservationNo);
        Assert.Equal(ReservationStatus.Rezerv, res.Durum);

        // 2) Tasfiye: kiraya çevir (KS-000001, Kirada; rezervasyon KirayaCevrildi)
        var rentalId = await reservations.ConvertToRentalAsync(resId);
        Assert.Equal(ReservationStatus.KirayaCevrildi, (await reservations.GetAsync(resId))!.Durum);
        var rental = await rentals.GetAsync(rentalId);
        DocumentNoOracle.OneOfExpected(1, 1, rental!.SozlesmeNo);
        Assert.Equal(RentalStatus.Kirada, rental.Durum);
        Assert.Equal(400m, rental.Tutar);

        // 3) Aynı araç/aralık ikinci kira artık ENGELLİ (aktif kira var)
        await Assert.ThrowsAsync<AvailabilityConflictException>(() => rentals.CreateDirectAsync(Input()));

        // 4) Teslim (çıkış 1000 km / yakıt 8)
        Assert.True(await rentals.DeliverAsync(rentalId, 1000, 8));

        // 5) Dönüş: 1 gün geç + 100 fazla km → uzatma 100 + fazla km 200 → GenelToplam 700
        Assert.True(await rentals.ReturnAsync(rentalId, returnKm: 1500, returnFuel: 8, actualReturn: bit.AddDays(1)));
        rental = await rentals.GetAsync(rentalId);
        Assert.Equal(RentalStatus.Tamamlandi, rental!.Durum);
        Assert.Equal(200m, rental.FazlaKmBedeli); // (1500-1000-400)=100 × 2
        Assert.Equal(100m, rental.UzatmaBedeli);   // 1 gün × 100
        Assert.Equal(700m, rental.GenelToplam);    // 400 + 200 + 100
        Assert.Equal(700m, rental.Bakiye);

        // 6) Fatura kes (GenelToplam 700 KDV-dahil → Borç Cari 700) → cari bakiye +700
        await invoices.CreateFromRentalAsync(rentalId);
        Assert.Equal(700m, await cash.GetAccountBalanceAsync(account));

        // 7) Nakit tahsilat 700 (kiraya bağlı) → Alacak Cari 700
        await cash.CollectAsync(new CashInput { CariId = account, RentalId = rentalId, Tutar = 700m });

        // 8) Mahsuplaşma: sözleşme tahsil edildi VE cari defter SIFIR (fatura↔tahsilat)
        rental = await rentals.GetAsync(rentalId);
        Assert.Equal(700m, rental!.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));

        // 9) Dönüş sonrası araç tekrar müsait → yeniden kiralanabilir
        var rentalId2 = await rentals.CreateDirectAsync(Input());
        Assert.NotEqual(Guid.Empty, rentalId2);
        DocumentNoOracle.OneOfExpected(1, 2, (await rentals.GetAsync(rentalId2))!.SozlesmeNo);   // boşluksuz no devam
    }
}
