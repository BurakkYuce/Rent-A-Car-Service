using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-71 — tarife gün-kademesi bazlı KM limiti + aşım ücreti.
///
/// <para><b>KULLANICI KARARI (oracle'ın temeli):</b> Km değeri GÜNLÜK limittir ve gün sayısına
/// ÇARPILIR — "günlük 200 girdin ve 5 günlük kiralama → müşteri 1000 km katedene kadar ek km
/// çıkmaz". Uzun dönem (haftalık/aylık) kademeleri Km6'ya DÜŞMEZ, kendi alanlarını kullanır.</para>
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen tutarlar elle hesaplanır (dahil km = limit × gün;
/// aşım = tahmini − dahil; tutar = aşım × ücret), motor kodundan türetilmez.</para>
///
/// <para><b>KAPSAM SINIRI:</b> bu değerler yalnız ÖN-İZLEME tahminini besler. Faturaya giren KM
/// aşımının tek otoritesi dönüş hesabıdır (ReturnMath) — bu faz oraya DOKUNMAZ.</para>
/// </summary>
[Collection("postgres")]
public sealed class TarifeKmKademeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2026, 4, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Araç grubu GLOBAL km değerleriyle (100 km/gün, 3 TL) — geriye uyum tabanı.</summary>
    private static async Task SeedGroupAsync(IServiceProvider sp)
        => await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 100, AsimKmUcreti = 3.00m });

    private static async Task<Guid> TariffAsync(IServiceProvider sp, Action<RateMatrixInput> configure)
    {
        var input = new RateMatrixInput
        {
            Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m,
            Gun6 = 1000m, Gun7 = 1000m, GunHaftalik = 900m, GunAylik = 800m,
            OnayDurumu = TariffApprovalStatus.Onayli
        };
        configure(input);
        return await sp.GetRequiredService<RateMatrixService>().CreateAsync(input);
    }

    private static Task<QuoteResult> QuoteAsync(IServiceProvider sp, int day, int estimatedKm)
        => sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        {
            AracGrupKod = "EKO", Kanal = "WEB",
            BasTar = Start, BitTar = Start.AddDays(day), TahminiKm = estimatedKm
        });

    [Fact]
    public async Task Km_limiti_GUNLUKTUR_gun_sayisina_carpilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);

        // ELLE: 5. kademe km = 200/gün, aşım 5 TL.
        await TariffAsync(sp, i => { i.Km5 = 200; i.Km5Ucret = 5.00m; });

        // 5 gün × 200 = 1000 km dahil. 1000 km'de aşım YOK (kullanıcının tarifi birebir).
        var full = await QuoteAsync(sp, 5, 1000);
        Assert.Equal(5, full.Gun);
        Assert.Equal(0m, full.KmAsimTutar);

        // 1 km fazlası → 1 × 5 = 5 TL (elle).
        Assert.Equal(5.00m, (await QuoteAsync(sp, 5, 1001)).KmAsimTutar);

        // 1500 km → aşım 500 × 5 = 2500 TL (elle).
        Assert.Equal(2500.00m, (await QuoteAsync(sp, 5, 1500)).KmAsimTutar);
    }

    [Fact]
    public async Task Her_kademe_KENDI_limitini_ve_ucretini_kullanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);

        // ELLE: 2. kademe 300 km/gün & 2 TL; 5. kademe 150 km/gün & 8 TL.
        await TariffAsync(sp, i =>
        {
            i.Km2 = 300; i.Km2Ucret = 2.00m;
            i.Km5 = 150; i.Km5Ucret = 8.00m;
        });

        // 2 gün, 1000 km → dahil 2×300 = 600; aşım 400 × 2 = 800 (elle).
        Assert.Equal(800.00m, (await QuoteAsync(sp, 2, 1000)).KmAsimTutar);

        // 5 gün, 1000 km → dahil 5×150 = 750; aşım 250 × 8 = 2000 (elle).
        // AYNI km, FARKLI kademe → farklı sonuç: kademe gerçekten kendi değerini kullanıyor.
        Assert.Equal(2000.00m, (await QuoteAsync(sp, 5, 1000)).KmAsimTutar);
    }

    [Fact]
    public async Task Uzun_donem_KENDI_alanlarini_kullanir_Km6ya_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);

        // ELLE: Km6 = 100/gün & 10 TL (kısa dönem), haftalık 400/gün & 1 TL, aylık 500/gün & 0,50 TL.
        await TariffAsync(sp, i =>
        {
            i.Km6 = 100; i.Km6Ucret = 10.00m;
            i.KmHaftalik = 400; i.KmHaftalikUcret = 1.00m;
            i.KmAylik = 500; i.KmAylikUcret = 0.50m;
        });

        // 10 gün (haftalık kademe), 5000 km → dahil 10×400 = 4000; aşım 1000 × 1 = 1000 (elle).
        // Km6'ya düşseydi: dahil 10×100 = 1000, aşım 4000 × 10 = 40.000 olurdu — kararın önemi bu.
        Assert.Equal(1000.00m, (await QuoteAsync(sp, 10, 5000)).KmAsimTutar);

        // 30 gün (aylık kademe), 20000 km → dahil 30×500 = 15000; aşım 5000 × 0,50 = 2500 (elle).
        Assert.Equal(2500.00m, (await QuoteAsync(sp, 30, 20000)).KmAsimTutar);
    }

    [Fact]
    public async Task Aylik_tanimsizsa_HAFTALIGA_duser_o_da_yoksa_kademeye()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);

        // ELLE: aylık YOK, haftalık 400/gün & 1 TL, Km6 100/gün & 10 TL.
        await TariffAsync(sp, i =>
        {
            i.Km6 = 100; i.Km6Ucret = 10.00m;
            i.KmHaftalik = 400; i.KmHaftalikUcret = 1.00m;
        });

        // 30 gün → aylık tanımsız → HAFTALIK: dahil 30×400 = 12000; aşım 3000 × 1 = 3000 (elle).
        Assert.Equal(3000.00m, (await QuoteAsync(sp, 30, 15000)).KmAsimTutar);
    }

    [Fact]
    public async Task Tarife_km_tasimiyorsa_ARAC_GRUBU_degerine_duser_geriye_uyum()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);

        // Tarifede HİÇ km alanı yok → grubun global değeri (100 km/gün, 3 TL) geçerli.
        await TariffAsync(sp, _ => { });

        // 4 gün, 1000 km → dahil 4×100 = 400; aşım 600 × 3 = 1800 (elle) — BUGÜNKÜ davranış.
        Assert.Equal(1800.00m, (await QuoteAsync(sp, 4, 1000)).KmAsimTutar);
    }

    [Fact]
    public async Task Bos_kademe_EN_YAKIN_dolu_kademeye_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);

        // ELLE: yalnız 3. kademe dolu (250/gün & 4 TL). 1., 2., 4-6. kademeler boş.
        await TariffAsync(sp, i => { i.Km3 = 250; i.Km3Ucret = 4.00m; });

        // 1 gün → kendi kademesi boş → en yakın dolu (Kademe 3): dahil 1×250; aşım 50 × 4 = 200.
        Assert.Equal(200.00m, (await QuoteAsync(sp, 1, 300)).KmAsimTutar);
        // 6 gün → aşağı doğru en yakın dolu yine Kademe 3: dahil 6×250 = 1500; aşım 100 × 4 = 400.
        Assert.Equal(400.00m, (await QuoteAsync(sp, 6, 1600)).KmAsimTutar);
    }

    [Fact]
    public async Task Km_limiti_SIFIR_veya_negatif_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // 0 limit "her km aşım" demek olur ve sessizce devasa aşım üretirdi; sınırsız için BOŞ bırakılır.
        await Assert.ThrowsAsync<ValidationException>(() => TariffAsync(sp, i => i.Km1 = 0));
        await Assert.ThrowsAsync<ValidationException>(() => TariffAsync(sp, i => i.KmAylik = -5));
        await Assert.ThrowsAsync<ValidationException>(() => TariffAsync(sp, i => i.Km2Ucret = -1m));
    }

    [Fact]
    public async Task Alanlar_create_ve_update_yolunda_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<RateMatrixService>();

        var id = await TariffAsync(sp, i =>
        {
            i.Km1 = 111; i.Km6 = 166; i.Km1Ucret = 1.25m; i.Km6Ucret = 6.75m;
            i.KmHaftalik = 400; i.KmHaftalikUcret = 0.90m;
            i.KmAylik = 500; i.KmAylikUcret = 0.45m;
        });

        var r = await svc.GetAsync(id);
        Assert.Equal(111, r!.Km1);
        Assert.Equal(166, r.Km6);
        Assert.Equal(1.25m, r.Km1Ucret);
        Assert.Equal(6.75m, r.Km6Ucret);
        Assert.Equal(400, r.KmHaftalik);
        Assert.Equal(0.45m, r.KmAylikUcret);

        // UPDATE yolu da yazmalı (yalnız create'e map etmek alanı sessizce düşürürdü).
        await svc.UpdateAsync(id, new RateMatrixInput
        {
            Kod = "EKO-WEB", Ad = "Eko Web", AracGrupKod = "EKO",
            Km1 = 222, KmAylik = 999, KmAylikUcret = 1.11m
        });
        var y = await svc.GetAsync(id);
        Assert.Equal(222, y!.Km1);
        Assert.Equal(999, y.KmAylik);
        Assert.Equal(1.11m, y.KmAylikUcret);
        Assert.Null(y.Km6);            // temizleme de bir güncellemedir
    }

    /// <summary>Bu faz ÖN-İZLEME tahminini değiştirir; sözleşmeye yazılan km alanlarına DOKUNMAZ.
    /// Faturaya giren aşımın tek otoritesi dönüş hesabıdır.</summary>
    [Fact]
    public async Task Kademe_km_sozlesmenin_KmLimit_alanini_DOLDURMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedGroupAsync(sp);
        await TariffAsync(sp, i => { i.Km3 = 250; i.Km3Ucret = 4.00m; });

        var vehicle = await sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>()
            .CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 KM 71", Grup = "EKO" });

        var input = new RentACar.Application.Bookings.BookingInput
        {
            VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3),
            Kaynak = "WEB", FiyatTuru = "Otomatik"
        };
        await sp.GetRequiredService<PricingService>().PriceAsync(input);

        // Sözleşmeye giden km alanları MANUEL kalır — motor bunlara DOKUNMAZ, girildiği gibi
        // (burada varsayılan 0) kalırlar. Doldursaydı, kademe tahmini sessizce faturaya sızardı.
        Assert.Equal(0, input.KmLimit);
        Assert.Equal(0m, input.FazlaKmUcret);

        // Elle girilen değer de KORUNUR (motor üzerine yazmaz).
        var manual = new RentACar.Application.Bookings.BookingInput
        {
            VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3),
            Kaynak = "WEB", FiyatTuru = "Otomatik", KmLimit = 777, FazlaKmUcret = 9.99m
        };
        await sp.GetRequiredService<PricingService>().PriceAsync(manual);
        Assert.Equal(777, manual.KmLimit);
        Assert.Equal(9.99m, manual.FazlaKmUcret);
    }
}
