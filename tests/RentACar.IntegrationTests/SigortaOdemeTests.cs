using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap J3 — sigorta ödeme+zeyil→defter (PARA). BAĞIMSIZ ORACLE: prim 1200 + zeyil 300 → Gider 1500
/// (Borç Gider/Alacak Kasa); kayıt Odendi + ZeyilPrim=300; çift-ödeme red; dönem-kilidi engeller.
/// </summary>
[Collection("postgres")]
public sealed class SigortaOdemeTests(PostgresFixture fx)
{
    private static async Task<(IServiceProvider sp, Guid polId)> Seed(IServiceScope scope, string plaka, decimal prim = 1200m)
    {
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        var pol = await sp.GetRequiredService<RegulationService>().AddInsuranceAsync(
            v, InsuranceType.Kasko, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), prim, "POL-1", "AnadoluSigorta", null);
        return (sp, pol);
    }

    [Fact]
    public async Task Ode_prim_arti_zeyil_gider()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, polId) = await Seed(scope, "34 SG 01");
        var reg = sp.GetRequiredService<RegulationService>();

        await reg.SigortaOdeAsync(polId, LedgerAccountType.Kasa, zeyilEkPrim: 300m);

        var gg = await sp.GetRequiredService<ReportService>().GetGelirGiderAsync();
        Assert.Equal(1500m, gg.GiderToplam);   // 1200 prim + 300 zeyil (elle oracle)
        Assert.Equal(0m, gg.GelirToplam);

        var rec = (await reg.ListInsuranceAsync()).Single(p => p.Id == polId);
        Assert.True(rec.Odendi);
        Assert.Equal(300m, rec.ZeyilPrim);

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => reg.SigortaOdeAsync(polId, LedgerAccountType.Kasa));
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, polId) = await Seed(scope, "34 SG 02");
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<RegulationService>().SigortaOdeAsync(polId, LedgerAccountType.Kasa));
    }
}
