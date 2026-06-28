using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Fiyat Motoru v1 (RentalQuoteEngine) — BAĞIMSIZ ORACLE. Beklenen tutarlar elle kurulmuş senaryodan
/// türetilir (motor kodundan DEĞİL). Tarife matrisi gün-kademesi + KM aşım + iskonto + sigorta + onay
/// akışı + tenant izolasyonu doğrulanır.
/// </summary>
[Collection("postgres")]
public sealed class FiyatMotoruTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static async Task SeedAsync(IServiceProvider sp, bool tarifeOnayli = true)
    {
        var vg = sp.GetRequiredService<VehicleGroupService>();
        await vg.CreateAsync(new VehicleGroupInput
        {
            Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5.00m,
            GencSurucuYas = 25, Provizyon = 2000.00m, MuafiyetTutari = 750.00m
        });

        var rm = sp.GetRequiredService<RateMatrixService>();
        await rm.CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, Gun4 = 875m, Gun5 = 850m, Gun6 = 825m, Gun7 = 800m,
            OnayDurumu = tarifeOnayli ? TarifeOnayDurumu.Onayli : TarifeOnayDurumu.Bekliyor
        });

        var rr = sp.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput
        {
            Kod = "EKO-KAMP", Ad = "Eko Kampanya", Kanal = "WEB", AracGrupKod = "EKO",
            MinGun = 3, Iskonto = 10.00m, HediyeGun = 0, KampanyaMi = true
        });

        var cp = sp.GetRequiredService<CoverageProductService>();
        await cp.CreateAsync(new CoverageProductInput
        { Kod = "SCDW", Ad = "Süper Muafiyet", Tur = CoverageProductType.Scdw, GunlukUcret = 120.00m, MaxGun = 30 });
        await cp.CreateAsync(new CoverageProductInput
        { Kod = "IMM", Ad = "İhtiyari Mali", Tur = CoverageProductType.Imm, GunlukUcret = 50.00m });
    }

    [Fact]
    public async Task Full_quote_matches_hand_computed_values()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope.ServiceProvider);
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // 5 gün (5×24h = 120h → ceil 5), genç sürücü (22<25), 2000 km, SCDW+IMM.
        var q = await engine.QuoteAsync(new QuoteRequest
        {
            AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(5),
            SurucuYas = 22, TahminiKm = 2000, SigortaUrunKodlari = ["SCDW", "IMM"]
        });

        // Elle oracle:
        //  gün=5 → kademe Gün5 = 850 günlük. Baz = 850×5 = 4250.
        //  KM: dahil = 300×5 = 1500; aşım = 2000−1500 = 500; ×5 = 2500.
        //  Sigorta: SCDW 120×min(5,30)=600; IMM 50×5=250 → 850.
        //  Ara = 4250+2500+850 = 7600. İskonto %10 = 760. Genel = 6840.
        Assert.Equal(5, q.Gun);
        Assert.Equal(850.00m, q.GunlukUcret);
        Assert.Equal(4250.00m, q.BazTutar);
        Assert.Equal(2500.00m, q.KmAsimTutar);
        Assert.Equal(850.00m, q.SigortaToplam);
        Assert.Equal(7600.00m, q.AraToplam);
        Assert.Equal(10.00m, q.IskontoOran);
        Assert.Equal(760.00m, q.IskontoTutar);
        Assert.Equal(6840.00m, q.GenelToplam);
        Assert.Equal(2000.00m, q.Provizyon);
        Assert.Equal(750.00m, q.Muafiyet);
        Assert.True(q.GencSurucu);
        Assert.Equal(2, q.SigortaKalemleri.Count);
    }

    [Fact]
    public async Task Day_tier_clamps_to_seven_for_long_rentals()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope.ServiceProvider);
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // 10 gün → kademe clamp 7 → Gün7 = 800 günlük. Baz = 800×10 = 8000. (KM/sigorta yok)
        // gün 10 ≥ MinGun(3) → iskonto %10 uygulanır: 8000×10% = 800 → Genel = 7200.
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(10) });

        Assert.Equal(10, q.Gun);
        Assert.Equal(800.00m, q.GunlukUcret);
        Assert.Equal(8000.00m, q.BazTutar);
        Assert.Equal(800.00m, q.IskontoTutar);
        Assert.Equal(7200.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Discount_rule_skipped_when_min_gun_not_met()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope.ServiceProvider);
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // 2 gün < MinGun(3) → iskonto kuralı UYGULANMAZ. Gün2 = 950 → Baz = 1900, iskonto 0.
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(2) });

        Assert.Equal(2, q.Gun);
        Assert.Equal(950.00m, q.GunlukUcret);
        Assert.Equal(1900.00m, q.BazTutar);
        Assert.Equal(0m, q.IskontoOran);
        Assert.Equal(0m, q.IskontoTutar);
        Assert.Equal(1900.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Unapproved_tariff_is_not_used_for_pricing()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope.ServiceProvider, tarifeOnayli: false); // Bekliyor
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(5) });

        Assert.Equal(0m, q.GunlukUcret);  // onaysız tarife kullanılmaz
        Assert.Equal(0m, q.BazTutar);
        Assert.Contains(q.Notlar, n => n.Contains("tarife matrisi bulunamadı"));
    }

    [Fact]
    public async Task Engine_respects_tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await SeedAsync(s1.ServiceProvider);

        // t2'de hiç master yok → motor t1 verisini GÖRMEZ (RLS).
        using var s2 = host.ScopeFor(t2);
        var engine = s2.ServiceProvider.GetRequiredService<RentalQuoteEngine>();
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(5) });

        Assert.Equal(0m, q.GunlukUcret);
        Assert.Equal(0m, q.GenelToplam);
    }

    // ---- Adversarial bulguları için regresyon (C1/M1/M2/M3) ----

    [Fact]
    public async Task Multi_currency_quote_rejected() // C1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope.ServiceProvider); // TRY tarife (ParaBirimi null → TRY)
        var cp = scope.ServiceProvider.GetRequiredService<CoverageProductService>();
        await cp.CreateAsync(new CoverageProductInput
        { Kod = "EURCOV", Ad = "EUR Teminat", Tur = CoverageProductType.Cdw, GunlukUcret = 100m, Doviz = "EUR" });
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // TRY tarife + EUR teminat tek teklifte → körlemesine toplama yerine reddedilmeli.
        await Assert.ThrowsAsync<ValidationException>(() => engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(2), SigortaUrunKodlari = ["EURCOV"] }));
    }

    [Fact]
    public async Task Tier_falls_back_upward_when_lower_tiers_empty() // M1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vg = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        await vg.CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var rm = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await rm.CreateAsync(new RateMatrixInput
        { Kod = "EKO-UP", Ad = "Üst", Kanal = "WEB", AracGrupKod = "EKO", Gun7 = 800m, OnayDurumu = TarifeOnayDurumu.Onayli });
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // 3 gün → kademe 3 boş; aşağı yok → YUKARI en yakın dolu = Gün7 = 800. Baz = 800×3 = 2400 (sıfır DEĞİL).
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(3) });
        Assert.Equal(800.00m, q.GunlukUcret);
        Assert.Equal(2400.00m, q.BazTutar);
    }

    [Fact]
    public async Task MaxGun_zero_is_uncapped_not_free() // M2
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope.ServiceProvider);
        var cp = scope.ServiceProvider.GetRequiredService<CoverageProductService>();
        await cp.CreateAsync(new CoverageProductInput
        { Kod = "ZEROCAP", Ad = "Tavansız", Tur = CoverageProductType.Cdw, GunlukUcret = 100m, MaxGun = 0 });
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // MaxGun=0 → bedava DEĞİL; tam gün faturalanır. 4 gün × 100 = 400.
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(4), SigortaUrunKodlari = ["ZEROCAP"] });
        Assert.Equal(400.00m, q.SigortaToplam);
        Assert.Equal(4, q.SigortaKalemleri[0].Gun);
    }

    [Fact]
    public async Task Most_generous_rule_wins_gift_over_small_discount() // M3
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vg = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        await vg.CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var rm = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await rm.CreateAsync(new RateMatrixInput
        {
            Kod = "FLAT", Ad = "Düz", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m, Gun6 = 1000m, Gun7 = 1000m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });
        var rr = scope.ServiceProvider.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput { Kod = "FREE3", Ad = "3 Hediye Gün", Kanal = "WEB", AracGrupKod = "EKO", HediyeGun = 3 });
        await rr.CreateAsync(new RentalRuleInput { Kod = "DISC5", Ad = "%5 İskonto", Kanal = "WEB", AracGrupKod = "EKO", Iskonto = 5m });
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // 5 gün, 1000/gün. FREE3 faydası = 3×1000 = 3000; DISC5 = 5×1000×5% = 250 → FREE3 kazanır.
        // Faturalanan = 5−3 = 2; Baz = 2000; iskonto 0 → Genel = 2000.
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(5) });
        Assert.Equal(3, q.HediyeGun);
        Assert.Equal(2000.00m, q.BazTutar);
        Assert.Equal(0m, q.IskontoTutar);
        Assert.Equal(2000.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Discount_rule_wins_when_coverage_makes_it_more_valuable() // M-NEW
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var vg = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        await vg.CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" }); // KM kuralı yok
        var rm = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await rm.CreateAsync(new RateMatrixInput
        {
            Kod = "FLAT", Ad = "Düz", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 100m, Gun2 = 100m, Gun3 = 100m, Gun4 = 100m, Gun5 = 100m, Gun6 = 100m, Gun7 = 100m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });
        var cp = scope.ServiceProvider.GetRequiredService<CoverageProductService>();
        await cp.CreateAsync(new CoverageProductInput
        { Kod = "BIGCOV", Ad = "Büyük Teminat", Tur = CoverageProductType.Scdw, GunlukUcret = 2000m });
        var rr = scope.ServiceProvider.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput { Kod = "HED1", Ad = "1 Hediye", Kanal = "WEB", AracGrupKod = "EKO", HediyeGun = 1 });
        await rr.CreateAsync(new RentalRuleInput { Kod = "ISK10", Ad = "%10", Kanal = "WEB", AracGrupKod = "EKO", Iskonto = 10m });
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        // 5 gün, 100/gün, BIGCOV 2000×5=10000 sigorta.
        //  HED1 faydası = 1×100 = 100. ISK10 faydası = (5×100 + 10000)×10% = 1050 → ISK10 kazanır.
        //  ISK10: hediye 0, baz 500, ara = 500+10000 = 10500, iskonto 1050 → Genel 9450.
        //  (Eski hatalı seçimde HED1 seçilip Genel 10400 olurdu — ~950 fazla.)
        var q = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = Bas, BitTar = Bas.AddDays(5), SigortaUrunKodlari = ["BIGCOV"] });

        Assert.Equal(0, q.HediyeGun);
        Assert.Equal(500.00m, q.BazTutar);
        Assert.Equal(10000.00m, q.SigortaToplam);
        Assert.Equal(10500.00m, q.AraToplam);
        Assert.Equal(1050.00m, q.IskontoTutar);
        Assert.Equal(9450.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Invalid_request_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var engine = scope.ServiceProvider.GetRequiredService<RentalQuoteEngine>();

        await Assert.ThrowsAsync<ValidationException>(() => engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "", BasTar = Bas, BitTar = Bas.AddDays(1) }));
        await Assert.ThrowsAsync<ValidationException>(() => engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas })); // bitiş <= başlangıç
    }
}
