using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira ekranından inline yeni müşteri: mevcut cari seçmeden, kirayla TEK adımda müşteri oluşturulup kira ona
/// bağlanır (ayrı ekranda cari açma zorunluluğu kalktı). Bağımsız oracle: yeni cari id = kira MusteriId + cari var.
/// </summary>
[Collection("postgres")]
public sealed class KiraInlineMusteriTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1).AddHours(9);

    [Fact]
    public async Task Yeni_bireysel_musteri_ile_kira_tek_akista_baglanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customers = sp.GetRequiredService<CustomerService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 INL 01" });

        // Kira ekranı akışı: mevcut cari SEÇİLMEDİ → yeni müşteri oluştur → kira ona bağlanır.
        var custId = await customers.CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "İnline", Soyad = "Müşteri", CepTel = "5551234567" });
        var rentalId = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = custId, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m });

        var rental = await rentals.GetAsync(rentalId);
        Assert.NotNull(rental);
        Assert.Equal(custId, rental!.MusteriId);            // kira yeni cariye bağlı
        Assert.NotNull(await customers.GetAsync(custId));   // cari gerçekten oluştu (tek akış)
    }

    [Fact]
    public async Task Yeni_kurumsal_musteri_unvanla_olusur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customers = sp.GetRequiredService<CustomerService>();

        // Ünvan verildiğinde kurumsal cari (kira ekranı inline oluşturma bunu Tip=Kurumsal yapar).
        var id = await customers.CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Yüce Kurumsal A.Ş.", VergiNo = "1234567890" });
        var c = await customers.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal(CustomerType.Kurumsal, c!.Tip);
        Assert.Equal("Yüce Kurumsal A.Ş.", c.Unvan);
    }
}
