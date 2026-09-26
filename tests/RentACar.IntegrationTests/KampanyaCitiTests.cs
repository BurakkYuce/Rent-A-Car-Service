using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A0 — KAMPANYA ÇİTİ (canlı bug düzeltmesi): KampanyaKodu'lu kural KOD GİRİLMEDEN otomatik
/// kural seçimine giremez. Düzeltme öncesi SelectRule kodu hiç okumuyordu → kod-kapılı %50 kampanya
/// her eşleşen teklife "en avantajlı" seçimiyle sessizce uygulanıyordu. BAĞIMSIZ ORACLE (elle):
/// matris düz 1000/gün, 3 gün → ara 3000; kurallar {GENEL %10 kodsuz, KAMP50 %50 kod=YAZ50} →
/// kodsuz teklif %10 → 3000−300=2700 (%50 SEÇİLMEZ). Kodsuz kampanya (KampanyaMi=true, kod yok)
/// otomatik kalır: %20'lik kodsuz kampanya %10 geneli yener → 2400. DAVRANIŞ DEĞİŞİKLİĞİ:
/// kod-kapılı kural artık yalnız kodla uygulanacak (A5 promosyon-kodu PR'ının ön koşulu).
/// </summary>
[Collection("postgres")]
public sealed class KampanyaCitiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static async Task SeedAsync(IServiceProvider sp)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5.00m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m, Gun6 = 1000m, Gun7 = 1000m,
            OnayDurumu = TariffApprovalStatus.Onayli
        });
    }

    private static Task<QuoteResult> QuoteAsync(IServiceProvider sp) =>
        sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Start, BitTar = Start.AddDays(3) });

    [Fact]
    public async Task Kod_kapili_kural_otomatik_secime_giremez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        var rr = sp.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput { Kod = "GENEL", Ad = "Genel", Iskonto = 10m });
        await rr.CreateAsync(new RentalRuleInput
        { Kod = "KAMP50", Ad = "Yaz Kampanyası", Iskonto = 50m, KampanyaMi = true, KampanyaKodu = "YAZ50" });

        // Elle: 3g × 1000 = 3000; kodsuz teklifte %10 (kod-kapılı %50 ÇİTE TAKILIR) → 2700.
        var q = await QuoteAsync(sp);
        Assert.Equal(3000.00m, q.AraToplam);
        Assert.Equal(10.00m, q.IskontoOran);
        Assert.Equal(2700.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Kodsuz_kampanya_otomatik_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        var rr = sp.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput { Kod = "GENEL", Ad = "Genel", Iskonto = 10m });
        // Kod ALANI OLMAYAN dönemsel kampanya: çite takılmaz, en avantajlı seçimle kazanır (mevcut davranış).
        await rr.CreateAsync(new RentalRuleInput
        { Kod = "DONEM20", Ad = "Dönemsel", Iskonto = 20m, KampanyaMi = true });

        // Elle: %20 > %10 → 3000 − 600 = 2400.
        var q = await QuoteAsync(sp);
        Assert.Equal(20.00m, q.IskontoOran);
        Assert.Equal(2400.00m, q.GenelToplam);
    }
}
