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
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plate)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Reprice", Soyad = "Probe" });
        return (m, v);
    }

    [Fact]
    public async Task Rezervasyon_duzenlemesi_net_modda_gross_up_u_TEKRAR_uygulamamali()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var res = sp.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(sp, "34 RP 01");

        var id = await res.CreateAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 1000m, FiyatTuru = "Günlük",
        });

        var created = (await res.GetAsync(id))!;
        Assert.Equal(1200m, created.GunlukUcret);   // 1.000 × 1,20 (elle)
        Assert.Equal(3600m, created.Tutar);         // 3 × 1.200 (elle)

        // Düzenleme ekranı ücreti KAYITLI değerle doldurur ve fiyat türünü aynen geri gönderir
        // (ReservationList.razor: value="@r.GunlukUcret" + <select name="fiyatTuru">).
        await res.UpdateAsync(id, new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(4),
            GunlukUcret = created.GunlukUcret, FiyatTuru = created.FiyatTuru,
        });

        var current = (await res.GetAsync(id))!;
        Assert.Equal(1200m, current.GunlukUcret);   // kullanıcı ücrete dokunmadı → DEĞİŞMEMELİ
        Assert.Equal(4800m, current.Tutar);         // 4 × 1.200 (elle)
    }
}
