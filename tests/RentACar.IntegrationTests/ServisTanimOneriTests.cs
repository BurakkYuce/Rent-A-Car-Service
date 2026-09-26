using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.ServisTanimlari;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-14 Bölüm C — servis tanımlarının filodaki GERÇEK kombinasyonlardan türetilmesi.
///
/// <para><b>Öneri HİÇBİR ŞEY YAZMAZ</b> — salt okuma; kabul ayrı bir adımdır. Testler bunu de
/// doğruluyor (öneri çağrısından sonra tanım sayısı değişmiyor).</para>
///
/// <para>Bağımsız oracle: kombinasyon sayıları ve adetler senaryodan elle yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class ServisTanimOneriTests(PostgresFixture fx)
{
    private static Task<Guid> VehicleAsync(IServiceProvider sp, string plate, string? brand, string? tip,
        FuelType? fuel = null, Transmission? transmission = null, VehicleStatus status = VehicleStatus.Musait)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = plate, Marka = brand, Tip = tip, Yakit = fuel, Vites = transmission, Durum = status });

    [Fact]
    public async Task Filodaki_FARKLI_kombinasyonlar_ELLE_beklenen_sayida_onerilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        // ELLE: 3 araçtan 2'si AYNI kombinasyon (Renault/Clio/Benzin/Manuel), 1'i farklı.
        await VehicleAsync(sp, "34 SO 01", "Renault", "Clio", FuelType.Benzin, Transmission.Manuel);
        await VehicleAsync(sp, "34 SO 02", " renault ", " clio ", FuelType.Benzin, Transmission.Manuel);  // harf/boşluk farkı
        await VehicleAsync(sp, "34 SO 03", "Fiat", "Egea", FuelType.Dizel, Transmission.Otomatik);

        var svc = sp.GetRequiredService<ServiceDefinitionService>();
        var suggestion = await svc.SuggestionAsync();

        Assert.Equal(2, suggestion.Count);                                  // ELLE: 2 farklı kombinasyon
        // TEMSİLCİ YAZIM DETERMİNİSTİK: "Renault" 1 kez, " renault " 1 kez → eşitlik → alfabetik
        // ilk (Ordinal'de büyük harf önce) = "Renault". Bu satır bir koşu-bağımlılığını kilitliyor:
        // önceden g.First() kullanılıyordu ve etiket DB satır sırasına göre değişiyordu.
        var renault = suggestion.Single(o => o.Kombinasyon.Marka == "Renault");
        Assert.Equal(2, renault.Kombinasyon.AracSayisi);               // ELLE: harf farkı TEK grup
        Assert.Equal("Clio", renault.Kombinasyon.Tip);
        Assert.Equal("Benzin", renault.Kombinasyon.Yakit);
        Assert.Equal("Manuel", renault.Kombinasyon.Vites);
        Assert.Equal(1, suggestion.Single(o => o.Kombinasyon.Marka == "Fiat").Kombinasyon.AracSayisi);

        // Öneri SALT OKUMA: hiçbir tanım doğmadı.
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Temsilci_yazim_EN_SIK_gecen_olur_ve_kosudan_kosuya_DEGISMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        // ELLE: "renault" 2 kez, "Renault" 1 kez → çoğunluk "renault" kazanmalı (alfabetik değil).
        await VehicleAsync(sp, "34 TM 01", "renault", "clio", FuelType.Benzin, Transmission.Manuel);
        await VehicleAsync(sp, "34 TM 02", "renault", "clio", FuelType.Benzin, Transmission.Manuel);
        await VehicleAsync(sp, "34 TM 03", "Renault", "Clio", FuelType.Benzin, Transmission.Manuel);

        var svc = sp.GetRequiredService<ServiceDefinitionService>();
        var first = Assert.Single(await svc.SuggestionAsync());
        Assert.Equal("renault", first.Kombinasyon.Marka);
        Assert.Equal("clio", first.Kombinasyon.Tip);
        Assert.Equal(3, first.Kombinasyon.AracSayisi);

        // Aynı sorgu tekrar çağrıldığında AYNI sonucu vermeli (sıra bağımlılığı yok).
        for (var i = 0; i < 3; i++)
        {
            var repeat = Assert.Single(await svc.SuggestionAsync());
            Assert.Equal(first.Kombinasyon.Marka, repeat.Kombinasyon.Marka);
            Assert.Equal(first.OnerilenKod, repeat.OnerilenKod);
        }
    }

    [Fact]
    public async Task Satilmis_ve_pasif_arac_ONERILMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await VehicleAsync(sp, "34 SO 04", "Opel", "Corsa", FuelType.Benzin, Transmission.Manuel);
        await VehicleAsync(sp, "34 SO 05", "Volvo", "XC40", FuelType.Dizel, Transmission.Otomatik, VehicleStatus.Satildi);
        await VehicleAsync(sp, "34 SO 06", "Skoda", "Fabia", FuelType.Benzin, Transmission.Manuel, VehicleStatus.Pasif);

        var suggestion = await sp.GetRequiredService<ServiceDefinitionService>().SuggestionAsync();
        Assert.Equal("Opel", Assert.Single(suggestion).Kombinasyon.Marka);   // ELLE: yalnız kiralanabilir olan
    }

    [Fact]
    public async Task Kombinasyona_bagli_tanim_varsa_ONERI_DUSER_kombinasyonsuz_tanim_SUSTURMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await VehicleAsync(sp, "34 SO 07", "Renault", "Clio", FuelType.Benzin, Transmission.Manuel);
        await VehicleAsync(sp, "34 SO 08", "Fiat", "Egea", FuelType.Dizel, Transmission.Otomatik);
        var svc = sp.GetRequiredService<ServiceDefinitionService>();

        // Kombinasyonu OLMAYAN eski tanım hiçbir öneriyi susturmamalı.
        await svc.CreateAsync(new ServisTanimInput { Kod = "ESKI", AracTipi = "Ekonomik", BakimKm = 15000 });
        Assert.Equal(2, (await svc.SuggestionAsync()).Count);

        // Kombinasyona bağlı tanım YALNIZ o kombinasyonu düşürür.
        await svc.CreateAsync(new ServisTanimInput
        {
            Kod = "RENCLIO", AracTipi = "Ekonomik", BakimKm = 15000,
            Marka = "renault", Tip = "CLIO", Yakit = "benzin", Vites = "manuel"   // harf duyarsız eşleşme
        });
        Assert.Equal("Fiat", Assert.Single(await svc.SuggestionAsync()).Kombinasyon.Marka);
    }

    [Fact]
    public async Task Onerilen_kod_MEVCUT_kodlarla_ve_kendi_icinde_CAKISMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceDefinitionService>();

        // İki kombinasyon AYNI kod tabanına düşecek şekilde kuruluyor (ilk 4 harf aynı).
        await VehicleAsync(sp, "34 SO 09", "Renault", "Clio", FuelType.Benzin, Transmission.Manuel);
        await VehicleAsync(sp, "34 SO 10", "Renault", "Clio", FuelType.Benzin, Transmission.Otomatik);
        await VehicleAsync(sp, "34 SO 11", "Renault", "Clio", FuelType.Dizel, Transmission.Manuel);

        var suggestion = await svc.SuggestionAsync();
        Assert.Equal(3, suggestion.Count);
        var codes = suggestion.Select(o => o.OnerilenKod).ToArray();
        Assert.Equal(3, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());   // hepsi tekil
        Assert.All(codes, k => Assert.True(k.Length <= 32));

        // Önerilen kodlar gerçekten kabul edilebilir olmalı (unique index'e takılmamalı).
        foreach (var o in suggestion)
            await svc.CreateAsync(new ServisTanimInput
            {
                Kod = o.OnerilenKod, AracTipi = o.Kombinasyon.Etiket, BakimKm = 15000,
                Marka = o.Kombinasyon.Marka, Tip = o.Kombinasyon.Tip,
                Yakit = o.Kombinasyon.Yakit, Vites = o.Kombinasyon.Vites
            });
        Assert.Equal(3, (await svc.ListAsync()).Count);
        Assert.Empty(await svc.SuggestionAsync());          // hepsi kapsandı
    }

    [Fact]
    public async Task Kombinasyon_alanlari_round_trip_ve_GUNCELLEMEDE_dusmuyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<ServiceDefinitionService>();

        var id = await svc.CreateAsync(new ServisTanimInput
        {
            Kod = "k1", AracTipi = " Ekonomik ", BakimKm = 15000,
            Marka = " BMW ", Tip = " 320i ", Yakit = " Benzin ", Vites = " Otomatik "
        });
        var t = await svc.GetAsync(id);
        Assert.Equal("K1", t!.Kod);
        Assert.Equal("BMW", t.Marka);          // trim
        Assert.Equal("320i", t.Tip);
        Assert.Equal("Benzin", t.Yakit);
        Assert.Equal("Otomatik", t.Vites);

        // KM güncellemesi kombinasyonu SIFIRLAMAMALI (form hepsini geri gönderiyor).
        await svc.UpdateAsync(id, new ServisTanimInput
        {
            Kod = "K1", AracTipi = "Ekonomik", BakimKm = 20000, Aktif = true,
            Marka = "BMW", Tip = "320i", Yakit = "Benzin", Vites = "Otomatik"
        });
        var t2 = await svc.GetAsync(id);
        Assert.Equal(20000, t2!.BakimKm);
        Assert.Equal("BMW", t2.Marka);
        Assert.Equal("Otomatik", t2.Vites);
    }

    [Fact]
    public async Task Oneri_yetki_ve_tenant_kapili()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
            await VehicleAsync(s1.ServiceProvider, "34 SO 12", "Gizli", "Model", FuelType.Benzin, Transmission.Manuel);

        // Başka tenant kombinasyonu GÖRMEZ.
        using (var s2 = host.ScopeFor(Guid.NewGuid()))
            Assert.Empty(await s2.ServiceProvider.GetRequiredService<ServiceDefinitionService>().SuggestionAsync());

        // Muhasebe rolünde OperationsWrite yok → öneri de kapalı (yazma yolunun ön adımı).
        using var acct = host.ScopeFor(t1, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            acct.ServiceProvider.GetRequiredService<ServiceDefinitionService>().SuggestionAsync());
    }
}
