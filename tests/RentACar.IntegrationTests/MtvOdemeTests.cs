using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap J1 — MTV ödeme→defter (PARA). BAĞIMSIZ ORACLE: 2000 MTV ödemesi → Gider 2000 (Borç Gider/Alacak Kasa),
/// kayıt Odendi; çift-ödeme red; dönem-kilidi engeller.
/// </summary>
[Collection("postgres")]
public sealed class MtvOdemeTests(PostgresFixture fx)
{
    private static async Task<(IServiceProvider sp, Guid mtvId)> Seed(IServiceScope scope, string plaka)
    {
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        var mtv = await sp.GetRequiredService<RegulationService>()
            .AddMtvAsync(v, "2026/1", 2000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        return (sp, mtv);
    }

    [Fact]
    public async Task Ode_gider_ve_odendi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, mtvId) = await Seed(scope, "34 MT 01");
        var reg = sp.GetRequiredService<RegulationService>();

        await reg.MtvOdeAsync(mtvId, LedgerAccountType.Kasa);

        var gg = await sp.GetRequiredService<ReportService>().GetGelirGiderAsync();
        Assert.Equal(2000m, gg.GiderToplam);   // Borç Gider 2000 (elle oracle)

        var rec = (await reg.ListMtvAsync()).Single(m => m.Id == mtvId);
        Assert.True(rec.Odendi);

        // Çift-ödeme reddedilir
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => reg.MtvOdeAsync(mtvId, LedgerAccountType.Kasa));
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, mtvId) = await Seed(scope, "34 MT 02");
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<RegulationService>().MtvOdeAsync(mtvId, LedgerAccountType.Kasa));
    }
}
