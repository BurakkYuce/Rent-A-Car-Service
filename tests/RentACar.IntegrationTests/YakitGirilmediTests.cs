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
    private static VehicleInput Vehicle(string plate, FuelType? fuel, Transmission transmission = Transmission.Manuel)
        => new()
        {
            Plaka = plate, Marka = "Fiat", Tip = "Egea", Vites = transmission, Yakit = fuel,
            Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        };

    [Fact]
    public async Task Yakit_verilmezse_NULL_kaydedilir_Benzin_DEGIL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await vehicles.CreateAsync(Vehicle("34 YK 01", fuel: null));

        // Eski davranış burada FuelType.Benzin döndürürdü — sessiz ve yanlış.
        Assert.Null((await vehicles.GetAsync(id))!.Yakit);

        // Açıkça Benzin seçmek HÂLÂ mümkün ve null'dan AYIRT EDİLEBİLİR.
        var id2 = await vehicles.CreateAsync(Vehicle("34 YK 02", FuelType.Benzin));
        Assert.Equal(FuelType.Benzin, (await vehicles.GetAsync(id2))!.Yakit);
    }

    [Fact]
    public async Task Yakiti_girilmemis_arac_girilmisle_AYNI_ilana_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await vehicles.CreateAsync(Vehicle("34 YK 10", FuelType.Dizel));
        await vehicles.CreateAsync(Vehicle("34 YK 11", fuel: null));

        // İmza yakıtı da kapsıyor → iki AYRI küme. Aksi halde yakıtı bilinmeyen araç, dizel ilanının
        // içine karışır ve site "Dizel" diye yayınlardı.
        var pool = await svc.PoolAsync();
        Assert.Equal(2, pool.Count);

        // Plaka SERVİSTE normalize ediliyor ("34 YK 11" → "34YK11"); ham metinle karşılaştırmak
        // testi sessizce hiçbir şey bulamaz hale getirir.
        var notEntered = Assert.Single(pool, k => k.Araclar.Any(v => v.Plaka == "34YK11"));
        // Başlıkta yakıt YAZMAZ: "Fiat Egea Manuel" — "Benzin" uydurulmaz.
        Assert.Equal("Fiat Egea Manuel", notEntered.Baslik);
        Assert.DoesNotContain("Benzin", notEntered.Baslik);

        var diesel = Assert.Single(pool, k => k.Araclar.Any(v => v.Plaka == "34YK10"));
        Assert.Equal("Fiat Egea Manuel Dizel", diesel.Baslik);
    }

    [Fact]
    public async Task Ozellik_listesinde_Yakit_satiri_HIC_olusmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await vehicles.CreateAsync(Vehicle("34 YK 20", fuel: null));
        var listingId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        var rows = await svc.SuggestedFeaturesAsync(listingId);
        // Boş değerli bir "Yakıt" satırı sitede anlamsız görünürdü; satır HİÇ üretilmez.
        Assert.DoesNotContain(rows, x => x.Etiket == "Yakıt");
        Assert.Contains(rows, x => x.Etiket == "Vites");   // girilmiş alanlar yerinde
    }

    [Fact]
    public async Task Yakit_sonradan_girilince_baslik_duzelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        var id = await vehicles.CreateAsync(Vehicle("34 YK 30", fuel: null));
        Assert.Equal("Fiat Egea Manuel", (await svc.PoolAsync()).Single().Baslik);

        // Personel araç ekranından doğru yakıtı giriyor.
        await vehicles.UpdateAsync(id, Vehicle("34 YK 30", FuelType.Dizel));

        Assert.Equal("Fiat Egea Manuel Dizel", (await svc.PoolAsync()).Single().Baslik);
    }
}
