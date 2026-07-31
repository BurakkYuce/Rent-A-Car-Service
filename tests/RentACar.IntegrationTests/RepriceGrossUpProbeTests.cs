using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PROBE — rezervasyon DÜZENLEMESİNDE net-mod ("Günlük") gross-up'ının tekrar uygulanıp
/// uygulanmadığı. Bağımsız oracle elle kurulur: 1.000 net @ %20 → 1.200 brüt/gün; 3 gün → 3.600.
/// Düzenlemede kullanıcı ücrete DOKUNMAZ (form kayıtlı 1.200'ü geri gönderir) ve yalnız bitiş
/// tarihini uzatır → 4 gün → beklenen 4.800. Günlük ücret 1.440'a çıkarsa gross-up İKİNCİ kez
/// uygulanmış demektir.
/// </summary>
[Collection("postgres")]
public sealed class RepriceGrossUpProbeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Reprice", Soyad = "Probe" });
        return (m, v);
    }

    [Fact]
    public async Task Rezervasyon_duzenlemesi_net_modda_gross_up_u_TEKRAR_uygulamamali()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rez = sp.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(sp, "34 RP 01");

        var id = await rez.CreateAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 1000m, FiyatTuru = "Günlük",
        });

        var olusan = (await rez.GetAsync(id))!;
        Assert.Equal(1200m, olusan.GunlukUcret);   // 1.000 × 1,20 (elle)
        Assert.Equal(3600m, olusan.Tutar);         // 3 × 1.200 (elle)

        // Düzenleme ekranı ücreti KAYITLI değerle doldurur ve fiyat türünü aynen geri gönderir
        // (ReservationList.razor: value="@r.GunlukUcret" + <select name="fiyatTuru">).
        await rez.UpdateAsync(id, new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(4),
            GunlukUcret = olusan.GunlukUcret, FiyatTuru = olusan.FiyatTuru,
        });

        var guncel = (await rez.GetAsync(id))!;
        Assert.Equal(1200m, guncel.GunlukUcret);   // kullanıcı ücrete dokunmadı → DEĞİŞMEMELİ
        Assert.Equal(4800m, guncel.Tutar);         // 4 × 1.200 (elle)
    }
}
