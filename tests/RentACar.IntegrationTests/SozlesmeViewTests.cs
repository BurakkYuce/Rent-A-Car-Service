using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Personnel;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR3 — SozlesmeView (sözleşme çıktısının TEK projeksiyonu; HTML-print + QuestPDF aynı modeli tüketir).
/// BAĞIMSIZ ORACLE: kullanılan km = 10.400 − 10.000 = 400 (elle); ehliyet decrypt'li düz gelir.
/// </summary>
[Collection("postgres")]
public sealed class SozlesmeViewTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-7).AddHours(9);

    [Fact]
    public async Task Sozlesme_view_tum_alanlar_ve_kullanilan_km()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var cari = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CariType.Bireysel, Ad = "Deneme", Soyad = "Musteri", CepTel = "05320000000",
            EhliyetNo = "35030", EhliyetSinifi = "B", EhliyetYeri = "BURDUR",
            DogumTarihi = new DateTimeOffset(1975, 4, 15, 0, 0, 0, TimeSpan.Zero)
        });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "07 BOP 605", Marka = "Fiat", Tip = "Egea", Km = 10000 });
        var pid = await sp.GetRequiredService<PersonelService>().CreateAsync(new PersonelInput
        { Kod = "P-SZ", Ad = "Onur", Soyad = "Yuce" });

        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 2m });
        await rentals.DeliverAsync(rental, cikisKm: 10000, cikisYakit: 8);
        await rentals.ReturnAsync(rental, donusKm: 10400, donusYakit: 6, Bas.AddDays(3),
            kmHediye: 50, bitisSebebi: "Normal", teslimAlanPersonelId: pid);

        var s = await sp.GetRequiredService<SozlesmeService>().GetAsync(rental);

        Assert.NotNull(s);
        Assert.Equal("Deneme Musteri", s!.MusteriAd);
        Assert.Equal("35030", s.EhliyetNo);          // decrypt'li düz değer
        Assert.Equal("BURDUR", s.EhliyetYeri);
        Assert.Equal("07BOP605", s.Plaka);
        Assert.Equal(10000, s.CikisKm);
        Assert.Equal(10400, s.DonusKm);
        Assert.Equal(400, s.KullanilanKm);           // 10.400 − 10.000 (elle oracle)
        Assert.Equal(50, s.KmHediye);
        Assert.Equal("Normal", s.BitisSebebi);
        Assert.Equal("Onur Yuce", s.TeslimAlanAd);
        // Para dökümü sözleşmedekiyle birebir: fazla = 400−300−50=50 × 2 = 100; toplam 300+100=400.
        Assert.Equal(100m, s.FazlaKmBedeli);
        Assert.Equal(400m, s.GenelToplam);
    }

    [Fact]
    public async Task Olmayan_kira_null_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<SozlesmeService>().GetAsync(Guid.NewGuid()));
    }
}
