using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Regulation;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap RETRO-fix — regülasyon ödeme giderleri araca atfedilir (J1-J3). BAĞIMSIZ ORACLE: MTV/muayene/sigorta
/// ödemesinde Gider satırı AccountRef = VehicleId taşır → araç/şube kârlılık raporu maliyeti araca bağlayabilir.
/// (RETRO adversarial Medium: önceden AccountRef=null idi, gider (Atanmamış)'a düşüyordu.)
/// </summary>
[Collection("postgres")]
public sealed class RegulasyonGiderAtfiTests(PostgresFixture fx)
{
    private static async Task<Guid?> GiderRef(IServiceProvider sp, string sourceType)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var e = await db.AccountLedgerEntries.AsNoTracking()
            .Where(x => x.SourceType == sourceType && x.AccountType == LedgerAccountType.Gider)
            .SingleAsync();
        return e.AccountRef;
    }

    [Fact]
    public async Task Mtv_gideri_araca_atfedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 GA 01", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();
        var mtv = await reg.AddMtvAsync(v, "2026/1", 2000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        await reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa);
        Assert.Equal(v, await GiderRef(sp, "MtvOdeme"));   // araca atıf (null değil)
    }

    [Fact]
    public async Task Muayene_gideri_araca_atfedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 GA 02", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();
        var insp = await reg.AddInspectionAsync(v, new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 1, 10, 0, 0, 0, TimeSpan.Zero), 500m);
        await reg.MuayeneOdeAsync(insp, LedgerAccountType.Kasa, ceza: 100m);
        Assert.Equal(v, await GiderRef(sp, "MuayeneOdeme"));
    }

    [Fact]
    public async Task Sigorta_gideri_araca_atfedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 GA 03", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await reg.AddInsuranceAsync(v, InsuranceType.Kasko, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), 1200m, "POL-1", "Sig", null);
        await reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa);
        Assert.Equal(v, await GiderRef(sp, "SigortaOdeme"));
    }
}
