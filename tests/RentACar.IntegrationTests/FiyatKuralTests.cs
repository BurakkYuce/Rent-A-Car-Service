using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G3 — fiyat motoru hafta sonu farkı (salt-hesap). BAĞIMSIZ ORACLE: 7 ardışık gün = KESİN 2 hafta
/// sonu günü (1 Cmt + 1 Pzr), başlangıç gününden bağımsız. Tüm tier=100 → ComputeGun-agnostik. Oran %10 →
/// fark = 100×2×0.10 = 20. Kural yoksa fark 0 (mevcut davranış korunur).
/// </summary>
[Collection("postgres")]
public sealed class FiyatKuralTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

    private static async Task SetupMatrix(IServiceProvider sp)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-WK", Ad = "Eko", AracGrupKod = "EKO",
            Gun1 = 100m, Gun2 = 100m, Gun3 = 100m, Gun4 = 100m, Gun5 = 100m, Gun6 = 100m, Gun7 = 100m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });
    }

    private static QuoteRequest Req() => new()
    { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas.AddDays(7), SigortaUrunKodlari = [] };

    [Fact]
    public async Task Weekend_surcharge_applied_when_rule_has_it()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SetupMatrix(sp);
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "WK", Ad = "Hafta sonu", AracGrupKod = "EKO", HaftaSonuFarkOran = 10m, Aktif = true });

        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(Req());

        Assert.Equal(100m, q.GunlukUcret);
        Assert.Equal(7, q.Gun);                  // 7 gün
        Assert.Equal(20m, q.HaftaSonuFark);      // 100 × 2 hafta sonu × %10 (elle oracle)
        Assert.Equal(720m, q.AraToplam);         // 700 baz + 20 hafta sonu
        Assert.Equal(720m, q.GenelToplam);
    }

    [Fact]
    public async Task No_rule_no_surcharge()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SetupMatrix(sp); // kural YOK

        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(Req());
        Assert.Equal(0m, q.HaftaSonuFark);
        Assert.Equal(700m, q.AraToplam); // yalnız baz (mevcut davranış korunur)
    }
}
