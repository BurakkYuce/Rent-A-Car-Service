using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.RezSartlar;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// <see cref="TestZaman"/> için kalıcı çit — tuzağı ve çözümü platformdan BAĞIMSIZ kanıtlar.
///
/// <para>Bu proje defalarca "lokalde yeşil, CI'da kırmızı" tarih testine düştü: PostgreSQL
/// <c>timestamptz</c> mikrosaniye saklar, Linux'ta <c>DateTimeOffset.UtcNow</c> 100ns tick üretir,
/// yazılan ile okunan değer eşit çıkmaz. macOS'ta üretilen değer zaten ~µs olduğu için tuzak
/// lokalde GÖRÜNMEZ. Aşağıdaki test tick'i ELLE kurduğu için her iki platformda da aynı sonucu
/// verir; <c>TestZaman.Simdi()</c> "basitleştirilirse" kırmızıya döner.</para>
/// </summary>
[Collection("postgres")]
public sealed class TestZamanTests(PostgresFixture fx)
{
    private static async Task<DateTimeOffset> YazOkuAsync(IServiceProvider sp, Guid musteri, DateTimeOffset t)
    {
        var svc = sp.GetRequiredService<ReservationTermService>();
        var id = await svc.CreateAsync(new RezSartInput { MusteriId = musteri, Sart = "zaman", TalepTarihi = t });
        return (await svc.GetAsync(id))!.TalepTarihi;
    }

    [Fact]
    public async Task Mikrosaniye_alti_tick_PG_round_tripinde_KAYBOLUR_saniye_hizali_KAYBOLMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var musteri = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Zaman", Soyad = "Testi" });

        // 1 tick = 100ns → µs'nin altında. PG bunu saklayamaz; yazılan ≠ okunan.
        var tickli = TestZaman.Simdi().AddTicks(3);
        Assert.NotEqual(tickli, await YazOkuAsync(sp, musteri, tickli));

        // TestZaman.Simdi() tam saniyeye hizalı → kayıpsız.
        var hizali = TestZaman.Simdi();
        Assert.Equal(hizali, await YazOkuAsync(sp, musteri, hizali));

        // Uzantı de aynı garantiyi vermeli.
        Assert.Equal(0, tickli.SaniyeyeHizala().Ticks % TimeSpan.TicksPerSecond);
    }
}
