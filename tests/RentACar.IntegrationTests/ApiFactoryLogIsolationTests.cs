using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Test altyapısı kilidi: bir <see cref="ApiFactory"/> host'u açılıp kapanınca aynı süreçteki Web test host'unun
/// Serilog statik logger'ı DEĞİŞMEZ. Api'nin AddSerilog'u statik logger'ı devralıp kapanışta kapatıyordu; ardından
/// koşan Web log testleri (UiIstemciHataTests) olayı bulamıyordu — sıra bağımlı, CI'da deterministik kırmızı.
/// </summary>
[Collection("web")]
public sealed class ApiFactoryLogIsolationTests(WebFixture fx)
{
    [Fact]
    public async Task Api_host_acilip_kapaninca_Web_statik_loggeri_degismez()
    {
        await fx.Web.Client().GetAsync("/api/ui/v1/oturum/xsrf"); // Web host kurulu, statik logger onun
        var before = Serilog.Log.Logger;
        using (var api = new ApiFactory(fx.Pg.AppConnectionString))
            await api.CreateClient().GetAsync("/api/v1/vehicles");
        Assert.Same(before, Serilog.Log.Logger);
    }
}
