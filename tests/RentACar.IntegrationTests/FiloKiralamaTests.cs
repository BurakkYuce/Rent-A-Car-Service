using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.FiloKiralamalar;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap L1 — filo/uzun-dönem kiralama. BAĞIMSIZ ORACLE: 1000 × 12 ay = 12000 net; kdv %20 = 2400;
/// genel toplam 14400; 12 taksit. No "FK-" boşluksuz. Tenant izolasyonu (racar_app, RLS).
/// </summary>
[Collection("postgres")]
public sealed class FiloKiralamaTests(PostgresFixture fx)
{
    private static async Task<(Guid cust, Guid veh)> Seed(IServiceProvider sp, string plaka)
    {
        var cust = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Filo Müşteri" });
        var veh = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        return (cust, veh);
    }

    [Fact]
    public async Task Create_no_and_taksit_plan()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cust, veh) = await Seed(sp, "34 FK 01");
        var svc = sp.GetRequiredService<FiloKiralamaService>();

        var id = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = cust, VehicleId = veh, SureAy = 12, AylikUcret = 1000m, KdvOrani = 0.20m });

        var k = await svc.GetAsync(id);
        Assert.StartsWith("FK-", k!.No);
        Assert.Equal(12, k.SureAy);

        var ozet = FiloKiralamaService.TaksitPlani(k);
        Assert.Equal(12, ozet.Taksitler.Count);
        Assert.Equal(12000m, ozet.ToplamNet);    // 1000 × 12 (elle oracle)
        Assert.Equal(2400m, ozet.ToplamKdv);     // 12000 × 0.20
        Assert.Equal(14400m, ozet.GenelToplam);  // 12000 + 2400
        // İlk taksit vadesi = başlangıç; 12. taksit = +11 ay.
        Assert.Equal(1200m, ozet.Taksitler[0].Toplam);
    }

    [Fact]
    public async Task SureAy_zero_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cust, veh) = await Seed(sp, "34 FK 02");
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            sp.GetRequiredService<FiloKiralamaService>().CreateAsync(new FiloKiralamaInput
            { MusteriId = cust, VehicleId = veh, SureAy = 0, AylikUcret = 1000m }));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        using (var a = host.ScopeFor(tenantA))
        {
            var (cust, veh) = await Seed(a.ServiceProvider, "34 FK 03");
            await a.ServiceProvider.GetRequiredService<FiloKiralamaService>()
                .CreateAsync(new FiloKiralamaInput { MusteriId = cust, VehicleId = veh, SureAy = 6, AylikUcret = 500m });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        var list = await b.ServiceProvider.GetRequiredService<FiloKiralamaService>().ListAsync();
        Assert.Empty(list); // başka tenant'ın sözleşmesi RLS ile gizli
    }
}
