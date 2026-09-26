using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Regulation;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Çok-döviz sigorta (küçük borç): AddInsurance artık Currency'yi set eder; ödeme kur ile baz
/// tutara çevirir. BAĞIMSIZ ORACLE: 1000 EUR prim × kur 40 = 40000 baz (elle türetilmiş sabit,
/// koddan değil). Regresyon: TRY poliçe kur 1 ile aynen kalır.
/// </summary>
[Collection("postgres")]
public sealed class SigortaDovizTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Eur_police_olusturur_ve_odemede_kur_ile_baza_cevirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 EU 01", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();

        // küçük harf "eur" → servis normalize → "EUR"
        var pol = await reg.AddInsuranceAsync(v, InsuranceType.Kasko, Bas, Bit, 1000m, "POL-EUR", "Sig", null, currencyCode: "eur");

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal("EUR", (await db.InsurancePolicies.AsNoTracking().SingleAsync(x => x.Id == pol)).Currency);

        await reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa, exchangeRate: 40m);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var entries = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => e.SourceType == "SigortaOdeme").ToListAsync();
            var gider = entries.Single(e => e.AccountType == LedgerAccountType.Gider);
            var kasa = entries.Single(e => e.AccountType == LedgerAccountType.Kasa);

            Assert.Equal("EUR", gider.Amount.Currency);
            Assert.Equal(1000m, gider.Amount.Amount);
            Assert.Equal(40m, gider.Amount.Rate);
            Assert.Equal(40000m, gider.Amount.AmountInBase); // 1000 EUR × 40 = 40000 baz (oracle)
            Assert.Equal(40000m, kasa.Amount.AmountInBase);  // denge: Σ borç(base) == Σ alacak(base)
        }
    }

    [Fact]
    public async Task Beyaz_liste_disi_doviz_reddedilir()
    {
        // Adversarial Low: crafted POST'la çöp/uzun döviz → 3-hane kolon DbUpdateException yerine
        // temiz ValidationException. Baz math'i etkilemez ama anlamsız/500 riski kapanır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 XY 01", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            reg.AddInsuranceAsync(v, InsuranceType.Kasko, Bas, Bit, 1000m, "P", "S", null, currencyCode: "ABCD")); // 4 hane
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            reg.AddInsuranceAsync(v, InsuranceType.Kasko, Bas, Bit, 1000m, "P", "S", null, currencyCode: "xyz"));  // bilinmeyen
    }

    [Fact]
    public async Task Dovizsiz_police_TRY_kalir_kur_bir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TR 01", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();

        var pol = await reg.AddInsuranceAsync(v, InsuranceType.Trafik, Bas, Bit, 500m, "POL-TRY", "Sig", null); // doviz yok → TRY

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal("TRY", (await db.InsurancePolicies.AsNoTracking().SingleAsync(x => x.Id == pol)).Currency);

        await reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa); // kur default 1

        await using (var db = await factory.CreateDbContextAsync())
        {
            var gider = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => e.SourceType == "SigortaOdeme" && e.AccountType == LedgerAccountType.Gider).SingleAsync();
            Assert.Equal("TRY", gider.Amount.Currency);
            Assert.Equal(500m, gider.Amount.AmountInBase); // 500 × 1 (regresyon: davranış değişmedi)
        }
    }
}
