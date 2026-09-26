using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 4.5 — OTA bileşen fiyat kolonları: rezervasyona persist (create + update), kira dönüşümünde
/// ReservationId geri-bağıyla erişilebilir (mega-form Web Api kutuları buradan okur); manuel kirada
/// kaynak rezervasyon YOK (kutular "—"). BİLGİ alanları — Tutar/defter etkilenmez (elle: 2g×100=200).
/// </summary>
[Collection("postgres")]
public sealed class OtaKolonlariTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2).AddHours(10); // rez geçmişe kapalı

    [Fact]
    public async Task Ota_alanlari_persist_donusumde_erisilir_ve_tutari_etkilemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 OT 01" });
        var musteri = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Ota", Soyad = "Cari" });
        var rez = sp.GetRequiredService<ReservationService>();

        var girdi = new BookingInput
        {
            MusteriId = musteri, VehicleId = arac, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m,
            Kaynak = "Web",
            OtaKiraBedeli = 90m, OtaDropBedeli = 15m, OtaBebekKoltugu = 5m, OtaNavigasyon = 4m,
            OtaLcf = 3m, OtaCdw = 12m, OtaScdw = 8m, OtaEkSurucu = 6m
        };
        var rid = await rez.CreateAsync(girdi);

        var r = await rez.GetAsync(rid);
        Assert.Equal(90m, r!.OtaKiraBedeli);
        Assert.Equal(15m, r.OtaDropBedeli);
        Assert.Equal(6m, r.OtaEkSurucu);
        Assert.Equal(200m, r.Tutar); // 2g×100 — OTA alanları fiyatı ETKİLEMEZ (elle)

        // Güncelleme: OTA alanları reprice'tan bağımsız güncellenir.
        girdi.OtaCdw = 20m;
        await rez.UpdateAsync(rid, girdi);
        Assert.Equal(20m, (await rez.GetAsync(rid))!.OtaCdw);

        // Dönüşüm: kira ReservationId geri-bağı taşır → mega-form OTA kutuları kaynaktan okur.
        await rez.ConfirmAsync(rid);
        var kiraId = await rez.ConvertToRentalAsync(rid);
        var kira = await sp.GetRequiredService<RentalService>().GetAsync(kiraId);
        Assert.Equal(rid, kira!.ReservationId);
        Assert.Equal(90m, (await rez.GetAsync(kira.ReservationId!.Value))!.OtaKiraBedeli);
    }

    [Fact]
    public async Task Manuel_kirada_kaynak_rezervasyon_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 OT 02" });
        var musteri = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Manuel", Soyad = "Cari" });

        var kiraId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = musteri, VehicleId = arac,
            BasTar = Bas.AddDays(-4), BitTar = Bas.AddDays(-2), GunlukUcret = 100m
        });
        Assert.Null((await sp.GetRequiredService<RentalService>().GetAsync(kiraId))!.ReservationId); // kutular "—"
    }
}
