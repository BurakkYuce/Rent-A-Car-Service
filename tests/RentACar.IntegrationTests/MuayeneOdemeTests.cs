using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap J2 — muayene ödeme+ceza→defter (PARA). BAĞIMSIZ ORACLE: ücret 500 + ceza 100 → Gider 600
/// (Borç Gider/Alacak Kasa); kayıt Odendi + Ceza=100; çift-ödeme red; dönem-kilidi engeller.
/// </summary>
[Collection("postgres")]
public sealed class MuayeneOdemeTests(PostgresFixture fx)
{
    private static async Task<(IServiceProvider sp, Guid inspId)> Seed(IServiceScope scope, string plaka, decimal ucret = 500m)
    {
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        var insp = await sp.GetRequiredService<RegulationService>().AddInspectionAsync(
            v, new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2028, 1, 10, 0, 0, 0, TimeSpan.Zero), ucret);
        return (sp, insp);
    }

    [Fact]
    public async Task Ode_ucret_arti_ceza_gider()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, inspId) = await Seed(scope, "34 MU 01");
        var reg = sp.GetRequiredService<RegulationService>();

        await reg.MuayeneOdeAsync(inspId, LedgerAccountType.Kasa, ceza: 100m);

        var gg = await sp.GetRequiredService<ReportService>().GetGelirGiderAsync();
        Assert.Equal(600m, gg.GiderToplam);   // 500 ücret + 100 ceza (elle oracle)
        Assert.Equal(0m, gg.GelirToplam);      // gelire sızmaz

        var rec = (await reg.ListInspectionAsync()).Single(m => m.Id == inspId);
        Assert.True(rec.Odendi);
        Assert.Equal(100m, rec.Ceza);

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => reg.MuayeneOdeAsync(inspId, LedgerAccountType.Kasa));
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, inspId) = await Seed(scope, "34 MU 02");
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<RegulationService>().MuayeneOdeAsync(inspId, LedgerAccountType.Kasa));
    }
}
