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

        var cari = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var bas = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var bit = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero); // 4 gün

        BookingInput Input() => new()
        {
            MusteriId = cari, VehicleId = vehicle, BasTar = bas, BitTar = bit,
            GunlukUcret = 100m, KmLimit = 400, FazlaKmUcret = 2m
        };

        // 1) Rezervasyon (RZ-000001, Rezerv)
        var resId = await reservations.CreateAsync(Input());
        var res = await reservations.GetAsync(resId);
        Assert.Equal("RZ-000001", res!.ReservationNo);
        Assert.Equal(ReservationStatus.Rezerv, res.Durum);

        // 2) Tasfiye: kiraya çevir (KS-000001, Kirada; rezervasyon KirayaCevrildi)
        var rentalId = await reservations.ConvertToRentalAsync(resId);
        Assert.Equal(ReservationStatus.KirayaCevrildi, (await reservations.GetAsync(resId))!.Durum);
        var rental = await rentals.GetAsync(rentalId);
        Assert.Equal("KS-000001", rental!.SozlesmeNo);
        Assert.Equal(RentalStatus.Kirada, rental.Durum);
        Assert.Equal(400m, rental.Tutar);

        // 3) Aynı araç/aralık ikinci kira artık ENGELLİ (aktif kira var)
        await Assert.ThrowsAsync<AvailabilityConflictException>(() => rentals.CreateDirectAsync(Input()));

        // 4) Teslim (çıkış 1000 km / yakıt 8)
        Assert.True(await rentals.DeliverAsync(rentalId, 1000, 8));

        // 5) Dönüş: 1 gün geç + 100 fazla km → uzatma 100 + fazla km 200 → GenelToplam 700
        Assert.True(await rentals.ReturnAsync(rentalId, donusKm: 1500, donusYakit: 8, gercekDonus: bit.AddDays(1)));
        rental = await rentals.GetAsync(rentalId);
        Assert.Equal(RentalStatus.Tamamlandi, rental!.Durum);
        Assert.Equal(200m, rental.FazlaKmBedeli); // (1500-1000-400)=100 × 2
        Assert.Equal(100m, rental.UzatmaBedeli);   // 1 gün × 100
        Assert.Equal(700m, rental.GenelToplam);    // 400 + 200 + 100
        Assert.Equal(700m, rental.Bakiye);

        // 6) Fatura kes (GenelToplam 700 KDV-dahil → Borç Cari 700) → cari bakiye +700
        await invoices.CreateFromRentalAsync(rentalId);
        Assert.Equal(700m, await cash.GetCariBalanceAsync(cari));

        // 7) Nakit tahsilat 700 (kiraya bağlı) → Alacak Cari 700
        await cash.CollectAsync(new CashInput { CariId = cari, RentalId = rentalId, Tutar = 700m });

        // 8) Mahsuplaşma: sözleşme tahsil edildi VE cari defter SIFIR (fatura↔tahsilat)
        rental = await rentals.GetAsync(rentalId);
        Assert.Equal(700m, rental!.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));

        // 9) Dönüş sonrası araç tekrar müsait → yeniden kiralanabilir
        var rentalId2 = await rentals.CreateDirectAsync(Input());
        Assert.NotEqual(Guid.Empty, rentalId2);
        Assert.Equal("KS-000002", (await rentals.GetAsync(rentalId2))!.SozlesmeNo); // boşluksuz no devam
    }
}
