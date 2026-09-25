using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-00 — BAF teslim tarihi. <c>BafService.TeslimAlAsync</c> imzası <c>donusTarihi</c>'ni ZATEN
/// alıyordu (varsayılan: şimdi) ama web ucu onu hiç geçmiyordu; form da sormuyordu. Sonuç: geç
/// girilen teslimlerde kayıtta <b>gerçek teslim anı değil kayıt anı</b> duruyordu.
///
/// Bağımsız oracle: beklenen değer senaryodan ("dün saat 14:00'te teslim aldım, dün yazsın"),
/// servisin kendi varsayılanından değil.
/// </summary>
[Collection("postgres")]
public sealed class BafDonusTarihiTests(PostgresFixture fx)
{
    private static async Task<Guid> BafAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        return await sp.GetRequiredService<BafService>().CreateAsync(new BafInput
        { PersonelId = Guid.NewGuid(), VehicleId = v, CikisKm = 10_000, Sube = "Merkez" });
    }

    [Fact]
    public async Task Verilen_donus_tarihi_KAYDA_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<BafService>();
        var id = await BafAsync(s.ServiceProvider, "34 BF 01");

        // Geçmişte bir teslim: personel bugün giriyor ama olay dün oldu.
        var dun = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-1).AddHours(14);
        Assert.True(await svc.TeslimAlAsync(id, donusKm: 10_500, donusYakit: 7, donusTarihi: dun));

        var b = await svc.GetAsync(id);
        Assert.Equal(dun, b!.DonusTarihi);
        Assert.Equal(7, b.DonusYakit);
    }

    [Fact]
    public async Task Tarih_verilmezse_SIMDI_yazilir_geriye_uyum()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<BafService>();
        var id = await BafAsync(s.ServiceProvider, "34 BF 02");

        var once = DateTimeOffset.UtcNow.AddSeconds(-5);
        Assert.True(await svc.TeslimAlAsync(id, donusKm: 10_400, donusYakit: null));

        var b = await svc.GetAsync(id);
        Assert.NotNull(b!.DonusTarihi);
        Assert.InRange(b.DonusTarihi!.Value, once, DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
