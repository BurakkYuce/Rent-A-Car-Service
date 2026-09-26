using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-21 — <c>Vehicle.Yakit</c> artık nullable ("girilmedi" ayırt edilebiliyor).
///
/// <para><b>Neden:</b> kolon NOT NULL + varsayılan <c>Benzin</c> idi. Yakıt seçilmeden kaydedilen
/// her araç sessizce "Benzin" oluyordu ve bu bilgi halka açık siteye, İLAN BAŞLIĞINA ve sözleşmeye
/// basılıyordu. Canlıda 26 aracın 26'sı "Benzin" görünüyordu — Fiat Egea/Fiorino filosunda bunun
/// doğru olması beklenmez. Yanlış bilgi yayınlamak, bilgi yayınlamamaktan kötüdür.</para>
///
/// Bağımsız oracle: beklenenler senaryodan kurulur ("yakıt girmedim → başlıkta yazmamalı"),
/// servisin kendi mantığından türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class YakitGirilmediTests(PostgresFixture fx)
{
    private static VehicleInput Arac(string plaka, FuelType? yakit, Transmission vites = Transmission.Manuel)
        => new()
        {
            Plaka = plaka, Marka = "Fiat", Tip = "Egea", Vites = vites, Yakit = yakit,
            Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        };

    [Fact]
    public async Task Yakit_verilmezse_NULL_kaydedilir_Benzin_DEGIL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await vehicles.CreateAsync(Arac("34 YK 01", yakit: null));

        // Eski davranış burada FuelType.Benzin döndürürdü — sessiz ve yanlış.
        Assert.Null((await vehicles.GetAsync(id))!.Yakit);

        // Açıkça Benzin seçmek HÂLÂ mümkün ve null'dan AYIRT EDİLEBİLİR.
        var id2 = await vehicles.CreateAsync(Arac("34 YK 02", FuelType.Benzin));
        Assert.Equal(FuelType.Benzin, (await vehicles.GetAsync(id2))!.Yakit);
    }

    [Fact]
    public async Task Yakiti_girilmemis_arac_girilmisle_AYNI_ilana_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await vehicles.CreateAsync(Arac("34 YK 10", FuelType.Dizel));
        await vehicles.CreateAsync(Arac("34 YK 11", yakit: null));

        // İmza yakıtı da kapsıyor → iki AYRI küme. Aksi halde yakıtı bilinmeyen araç, dizel ilanının
        // içine karışır ve site "Dizel" diye yayınlardı.
        var havuz = await svc.PoolAsync();
        Assert.Equal(2, havuz.Count);

        // Plaka SERVİSTE normalize ediliyor ("34 YK 11" → "34YK11"); ham metinle karşılaştırmak
        // testi sessizce hiçbir şey bulamaz hale getirir.
        var girilmemis = Assert.Single(havuz, k => k.Araclar.Any(v => v.Plaka == "34YK11"));
        // Başlıkta yakıt YAZMAZ: "Fiat Egea Manuel" — "Benzin" uydurulmaz.
        Assert.Equal("Fiat Egea Manuel", girilmemis.Baslik);
        Assert.DoesNotContain("Benzin", girilmemis.Baslik);

        var dizel = Assert.Single(havuz, k => k.Araclar.Any(v => v.Plaka == "34YK10"));
        Assert.Equal("Fiat Egea Manuel Dizel", dizel.Baslik);
    }

    [Fact]
    public async Task Ozellik_listesinde_Yakit_satiri_HIC_olusmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await vehicles.CreateAsync(Arac("34 YK 20", yakit: null));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        var satirlar = await svc.SuggestedFeaturesAsync(ilanId);
        // Boş değerli bir "Yakıt" satırı sitede anlamsız görünürdü; satır HİÇ üretilmez.
        Assert.DoesNotContain(satirlar, x => x.Etiket == "Yakıt");
        Assert.Contains(satirlar, x => x.Etiket == "Vites");   // girilmiş alanlar yerinde
    }

    [Fact]
    public async Task Yakit_sonradan_girilince_baslik_duzelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        var id = await vehicles.CreateAsync(Arac("34 YK 30", yakit: null));
        Assert.Equal("Fiat Egea Manuel", (await svc.PoolAsync()).Single().Baslik);

        // Personel araç ekranından doğru yakıtı giriyor.
        await vehicles.UpdateAsync(id, Arac("34 YK 30", FuelType.Dizel));

        Assert.Equal("Fiat Egea Manuel Dizel", (await svc.PoolAsync()).Single().Baslik);
    }
}
