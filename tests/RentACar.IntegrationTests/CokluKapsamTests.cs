using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-70 — Çoklu-seçim kapsam alanları (CSV) + tarife "Max Kira Kapsamı" elemesi.
///
/// <para><b>Bu fazın gerçek davranış değişikliği tek yerde:</b> tarife satırının
/// <c>KiraSuresi</c>'ni aşan kiralarda o satır ADAYLIKTAN ELENİR ve motor sıradaki uygun satıra
/// düşer. Aşağıdaki test bunu iki satırla kanıtlıyor: kısa kira A satırıyla, uzun kira B satırıyla
/// fiyatlanıyor — ve elenen satır için not düşülüyor.</para>
///
/// <para><c>BrokerYasakService.KapsarMi</c>'nin bu sürümde ÇAĞIRANI YOKTUR (bilinçli; bkz. metot
/// özeti). Burada birim testi var çünkü CSV biçiminin okuma sözleşmesini sabitliyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class CokluKapsamTests(PostgresFixture fx)
{
    // ---------- CSV sözleşmesi ----------

    [Theory]
    [InlineData("EKO,STD,LUX", "STD", true)]
    [InlineData("EKO,STD,LUX", "std", true)]      // harf duyarsız
    [InlineData("EKO, STD , LUX", "LUX", true)]   // boşluklu CSV
    [InlineData("EKO,STD,LUX", "SUV", false)]
    [InlineData("EKO", "EKO", true)]              // tek değer (eski kayıt) geriye uyumlu
    [InlineData(null, "EKO", false)]              // kısıt yok ≠ her şeyi kapsar
    [InlineData("", "EKO", false)]
    [InlineData("EKO,STD", null, false)]
    public void KapsarMi_sozlesmesi(string? csv, string? value, bool expected)
        => Assert.Equal(expected, BrokerBanService.IsCovered(csv, value));

    [Fact]
    public async Task Coklu_secim_CSVye_normalize_edilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<BrokerBanService>();

        // CokluSecim bileşeni aynı adı taşıyan alanları gönderir → uca "eko, std ,EKO" gibi gelir.
        var id = await svc.CreateAsync(new BrokerYasakInput
        {
            Kod = "BRK1", Ad = "Çoklu kapsam", TumSatisKapali = true,
            AracGrupKod = "eko, std ,EKO,  ",          // tekrar + boşluk + boş parça
            Bolge = "Antalya, İzmir , Antalya"
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        // ELLE beklenen: büyük harf, tekrarsız, sıra korunmuş.
        Assert.Equal("EKO,STD", r!.AracGrupKod);
        // Bölge büyük harfe ÇEVRİLMEZ (şehir adı; "İzmir" bozulmamalı) ama tekrarı temizlenir.
        Assert.Equal("Antalya,İzmir", r.Bolge);

        // Yazılan biçim okuma sözleşmesiyle uyumlu olmalı.
        Assert.True(BrokerBanService.IsCovered(r.AracGrupKod, "STD"));
        Assert.True(BrokerBanService.IsCovered(r.Bolge, "izmir"));
        Assert.False(BrokerBanService.IsCovered(r.AracGrupKod, "LUX"));
    }

    [Fact]
    public async Task Bos_kapsam_null_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<BrokerBanService>();

        var id = await svc.CreateAsync(new BrokerYasakInput
        { Kod = "BRK2", Ad = "Kapsamsız", TumSatisKapali = true, AracGrupKod = " , , ", Bolge = "" });
        var r = await svc.GetAsync(id);
        Assert.Null(r!.AracGrupKod);   // "hepsi" anlamı korunur
        Assert.Null(r.Bolge);
    }

    // ---------- Tarife: Max Kira Kapsamı elemesi ----------

    [Fact]
    public async Task Max_kira_kapsamini_ASAN_kira_satiri_ELER_ve_digerine_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>()
            .CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var mat = sp.GetRequiredService<RateMatrixService>();

        // A: kısa kiralar için (max 14 gün) günlük 1.000
        _ = await mat.CreateAsync(new RateMatrixInput
        {
            Kod = "A-KISA", Ad = "Kısa dönem", AracGrupKod = "EKO", KiraSuresi = 14,
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m, Gun6 = 1000m, Gun7 = 1000m,
            GunHaftalik = 1000m, GunAylik = 1000m, OnayDurumu = TariffApprovalStatus.Onayli
        });
        // B: sınırsız (KiraSuresi null) günlük 700
        _ = await mat.CreateAsync(new RateMatrixInput
        {
            Kod = "B-UZUN", Ad = "Uzun dönem", AracGrupKod = "EKO",
            Gun1 = 700m, Gun2 = 700m, Gun3 = 700m, Gun4 = 700m, Gun5 = 700m, Gun6 = 700m, Gun7 = 700m,
            GunHaftalik = 700m, GunAylik = 700m, OnayDurumu = TariffApprovalStatus.Onayli
        });

        var motor = sp.GetRequiredService<RentalQuoteEngine>();
        var start = TestZaman.Now().AddDays(1);

        // 10 gün: A hâlâ aday (10 ≤ 14) → sıralama kuralı A'yı seçer (Kod alfabetik ilk).
        var brief = await motor.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = start, BitTar = start.AddDays(10) });
        Assert.Equal(1000m, brief.GunlukUcret);
        Assert.DoesNotContain(brief.Notlar, n => n.Contains("elendi"));

        // 20 gün: A elenir (20 > 14) → B ile fiyatlanır ve NOT düşülür.
        var longText = await motor.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = start, BitTar = start.AddDays(20) });
        Assert.Equal(700m, longText.GunlukUcret);
        Assert.Contains(longText.Notlar, n => n.Contains("A-KISA") && n.Contains("elendi"));
    }

    [Fact]
    public async Task Tek_satir_elenirse_TARIFE_BULUNAMADI_akisina_duser_yeni_red_yolu_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>()
            .CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var mat = sp.GetRequiredService<RateMatrixService>();

        _ = await mat.CreateAsync(new RateMatrixInput
        {
            Kod = "TEK", Ad = "Tek satır", AracGrupKod = "EKO", KiraSuresi = 5,
            Gun1 = 900m, Gun2 = 900m, Gun3 = 900m, Gun4 = 900m, Gun5 = 900m, Gun6 = 900m, Gun7 = 900m,
            GunHaftalik = 900m, GunAylik = 900m, OnayDurumu = TariffApprovalStatus.Onayli
        });

        var start = TestZaman.Now().AddDays(1);
        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = start, BitTar = start.AddDays(30) });

        // İstisna ATILMAZ: mevcut "tarife bulunamadı" davranışı korunur (sessiz-güvenli).
        Assert.Equal(0m, q.GunlukUcret);
        Assert.Contains(q.Notlar, n => n.Contains("elendi"));
        Assert.Contains(q.Notlar, n => n.Contains("bulunamadı"));
    }

    [Fact]
    public async Task Turu_ve_KiraSuresi_round_trip_gecersiz_deger_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var mat = s.ServiceProvider.GetRequiredService<RateMatrixService>();

        var id = await mat.CreateAsync(new RateMatrixInput
        { Kod = "K1", Ad = "Kampanya", Turu = " Kampanya ", KiraSuresi = 7, Lokasyon = "Antalya, İzmir", Gun1 = 500m });
        var r = await mat.GetAsync(id);
        Assert.Equal("Kampanya", r!.Turu);        // trim
        Assert.Equal(7, r.KiraSuresi);
        Assert.Equal("Antalya, İzmir", r.Lokasyon);

        // 0/negatif max gün hiçbir kirayı kapsamaz → satır ölü doğar, reddedilir.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => mat.CreateAsync(
            new RateMatrixInput { Kod = "K0", Ad = "Sıfır", KiraSuresi = 0, Gun1 = 100m }));
        Assert.Contains("Max kira kapsamı", ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => mat.CreateAsync(
            new RateMatrixInput { Kod = "KN", Ad = "Negatif", KiraSuresi = -3, Gun1 = 100m }));
    }
}
