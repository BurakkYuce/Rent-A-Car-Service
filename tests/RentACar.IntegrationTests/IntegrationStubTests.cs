using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>v1 entegrasyon stub'ları kayıtlı + çağrılabilir (DB gerektirmez).</summary>
public sealed class IntegrationStubTests
{
    private static ServiceProvider Build()
        => new ServiceCollection().AddIntegrationStubs().BuildServiceProvider();

    [Fact]
    public void All_ports_resolve()
    {
        using var sp = Build();
        Assert.NotNull(sp.GetService<ISmsService>());
        Assert.NotNull(sp.GetService<IWhatsAppService>());
        Assert.NotNull(sp.GetService<IGoogleCalendarService>());
        Assert.NotNull(sp.GetService<IEInvoiceService>());
        Assert.NotNull(sp.GetService<IPosService>());
        Assert.NotNull(sp.GetService<IKabisService>());
        Assert.NotNull(sp.GetService<IHgsService>());
    }

    [Fact]
    public async Task Einvoice_stub_returns_ettn()
    {
        using var sp = Build();
        var result = await sp.GetRequiredService<IEInvoiceService>()
            .SendAsync(new EInvoiceRequest("1234567890", "ACME", 100m, 20m, "TRY"));
        Assert.True(result.Success);
        Assert.NotNull(result.Ettn);
    }

    [Fact]
    public async Task Pos_authorize_capture_flow_stub()
    {
        using var sp = Build();
        var pos = sp.GetRequiredService<IPosService>();
        var auth = await pos.AuthorizeAsync(new PosCharge(500m, "TRY", "tok_x", ThreeD: true));
        Assert.True(auth.Success);
        var capture = await pos.CaptureAsync(auth.TxRef!, 500m);
        Assert.True(capture.Success);
    }
}
