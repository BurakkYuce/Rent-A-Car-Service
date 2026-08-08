using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Currencies;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-20 — Araç Grubu / Döviz / Hesap No sözlük derinliği.
///
/// <para>Bağımsız oracle: alan değerleri testte elle verilip aynen geri okunur; araç sayısı
/// senaryodan sayılır ("3 aracı EKO grubuna atadım → 3 beklerim"), servisin kendi dönüşünden
/// türetilmez.</para>
///
/// <para><b>Bu fazın tekrar eden tuzağı:</b> üç servisin de <c>Normalize()</c> metodu YENİ bir input
/// nesnesi kuruyor. Yeni alanı oraya eklemeyi atlamak alanı sessizce kaybettirir (form doldurulur,
/// kaydedilir, boş döner) ve HİÇBİR derleme hatası vermez. Aşağıdaki round-trip testleri bunun tek
/// savunmasıdır.</para>
/// </summary>
[Collection("postgres")]
public sealed class SozlukDerinlikTests(PostgresFixture fx)
{
    // ---------- Araç grubu ----------

    [Fact]
    public async Task Arac_grubu_yeni_7_alan_create_update_turunda_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await svc.CreateAsync(new VehicleGroupInput
        {
            Kod = "EKO", Ad = "Ekonomik",
            ProvizyonDoviz = " eur ", Provizyon2Doviz = "usd",
            YakitTuru = FuelType.Dizel, Vites = Vites.Otomatik,
            EntegrasyonKod1 = " BRK-ECO ", WebId = "web-1", ServisId = "srv-1"
        });

        var g = await svc.GetAsync(id);
        Assert.NotNull(g);
        Assert.Equal("EUR", g!.ProvizyonDoviz);        // döviz kodu trim + büyük harf
        Assert.Equal("USD", g.Provizyon2Doviz);
        Assert.Equal(FuelType.Dizel, g.YakitTuru);
        Assert.Equal(Vites.Otomatik, g.Vites);
        Assert.Equal("BRK-ECO", g.EntegrasyonKod1);    // trim
        Assert.Equal("web-1", g.WebId);
        Assert.Equal("srv-1", g.ServisId);

        await svc.UpdateAsync(id, new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", WebId = "web-2", YakitTuru = FuelType.Benzin });
        var u = await svc.GetAsync(id);
        Assert.Equal("web-2", u!.WebId);
        Assert.Equal(FuelType.Benzin, u.YakitTuru);
        Assert.Null(u.ProvizyonDoviz);                 // verilmeyen alan temizlenir (Apply tam yazar)
    }

    [Fact]
    public async Task Yeni_alanlar_verilmeden_eski_cagri_bicimi_CALISIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await svc.CreateAsync(new VehicleGroupInput { Kod = "STD", Ad = "Standart", Provizyon = 5000m });
        var g = await svc.GetAsync(id);
        Assert.Equal(5000m, g!.Provizyon);
        Assert.Null(g.ProvizyonDoviz);
        Assert.Null(g.YakitTuru);
        Assert.Null(g.Vites);
        Assert.Null(g.WebId);
    }

    [Fact]
    public async Task Arac_sayisi_ELLE_ATANAN_kadar_ve_Turkce_buyuk_kucuk_harf_duyarsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var gruplar = sp.GetRequiredService<VehicleGroupService>();
        var araclar = sp.GetRequiredService<VehicleService>();

        var eko = await gruplar.CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        var lux = await gruplar.CreateAsync(new VehicleGroupInput { Kod = "LUX", Ad = "Lüks" });

        // ELLE: 3 araç Ekonomik'e (biri farklı harf yazımıyla), 1 araç Lüks'e, 1 araç grupsuz.
        await araclar.CreateAsync(new VehicleInput { Plaka = "34 SD 01", Grup = "Ekonomik" });
        await araclar.CreateAsync(new VehicleInput { Plaka = "34 SD 02", Grup = "EKONOMİK" });
        await araclar.CreateAsync(new VehicleInput { Plaka = "34 SD 03", Grup = "ekonomik" });
        await araclar.CreateAsync(new VehicleInput { Plaka = "34 SD 04", Grup = "Lüks" });
        await araclar.CreateAsync(new VehicleInput { Plaka = "34 SD 05" });

        var sayilar = await gruplar.AracSayilariAsync();
        Assert.Equal(3, sayilar[eko]);
        Assert.Equal(1, sayilar[lux]);

        // Sayaç ile "eşleşmeyen değerler" paneli AYNI kuralı kullanmalı: 3+1 araç eşleşti,
        // geriye yalnız grubu BOŞ olan 1 araç kalır.
        var eslesmeyen = await gruplar.ListUnmatchedGrupValuesAsync();
        Assert.Equal(1, eslesmeyen.Single(x => x.Bos).AracSayisi);
        Assert.DoesNotContain(eslesmeyen, x => !x.Bos);
    }

    // ---------- Döviz ----------

    [Fact]
    public async Task Doviz_Ulke_round_trip_ve_KUR_ALANI_YOK()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<CurrencyService>();

        var id = await svc.CreateAsync(new CurrencyInput { Kod = "USD", Ad = "Amerikan Doları", Sembol = "$", Ulke = "  ABD  " });
        var c = (await svc.ListAsync()).Single(x => x.Id == id);
        Assert.Equal("ABD", c.Ulke);   // trim

        await svc.UpdateAsync(id, new CurrencyInput { Kod = "USD", Ad = "Amerikan Doları", Ulke = "Amerika", Aktif = true });
        Assert.Equal("Amerika", (await svc.ListAsync()).Single(x => x.Id == id).Ulke);

        // Sözleşme kilidi: Currency'ye KUR alanı EKLENMEMELİ. Kur tek kaynaktan (/kurlar: TCMB +
        // tenant sabitleme) yönetiliyor; buraya ikinci bir kur alanı çift-kaynak yaratırdı.
        var kurAlanlari = typeof(RentACar.Domain.Entities.Currency).GetProperties()
            .Where(p => p.Name.Contains("Kur", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name).ToList();
        Assert.Empty(kurAlanlari);
    }

    // ---------- Hesap No ----------

    [Fact]
    public async Task Hesap_HediyeCek_OzelKod_UyariMail_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<FinancialAccountService>();

        var id = await svc.CreateAsync(new FinancialAccountInput
        {
            Kod = "HC1", Ad = "Hediye Çeki Hesabı", Tur = "Kasa", Sube = "Merkez",
            HediyeCek = true, OzelKod = " hc ", UyariMailListesi = " a@firma.test, b@firma.test "
        });

        var a = (await svc.ListAsync()).Single(x => x.Id == id);
        Assert.True(a.HediyeCek);
        Assert.Equal("HC", a.OzelKod);                                  // trim + büyük harf
        Assert.Equal("a@firma.test, b@firma.test", a.UyariMailListesi); // trim (iç biçim korunur)
        Assert.Equal("Merkez", a.Sube);                                 // mevcut alan bozulmadı

        // Kapatılabilmeli (bool alanın false'a dönmesi Normalize'da kaybolmamalı).
        await svc.UpdateAsync(id, new FinancialAccountInput
        { Kod = "HC1", Ad = "Hediye Çeki Hesabı", Tur = "Kasa", HediyeCek = false, Aktif = true });
        var g = (await svc.ListAsync()).Single(x => x.Id == id);
        Assert.False(g.HediyeCek);
        Assert.Null(g.OzelKod);
        Assert.Null(g.UyariMailListesi);
    }

    [Fact]
    public async Task Sozluk_alanlari_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
        {
            await s1.ServiceProvider.GetRequiredService<VehicleGroupService>()
                .CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik", WebId = "gizli" });
            await s1.ServiceProvider.GetRequiredService<CurrencyService>()
                .CreateAsync(new CurrencyInput { Kod = "USD", Ad = "Dolar", Ulke = "ABD" });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<VehicleGroupService>().ListAsync());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<CurrencyService>().ListAsync());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<VehicleGroupService>().AracSayilariAsync());
    }
}
