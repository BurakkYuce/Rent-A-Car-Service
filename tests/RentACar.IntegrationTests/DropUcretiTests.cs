using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.DropTanimlari;
using RentACar.Application.RentalAddOns;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A3b — Drop ücreti: kira DonusOfisi != CikisOfisi ve aktif DropTanim.Lokasyon == DonusOfisi
/// (Ucret &gt; 0) ise SYS-DROP sistem satırı (TEK SEFERLİK — Miktar=1, gün ile ölçeklenmez).
/// BAĞIMSIZ ORACLE (elle): 1000×3g=3000; drop tanımı 500 NET → satır 500/100/600 brüt → 3600.
/// Manuel override (BookingInput.DropUcreti=750 NET) tanımı ezer → 900 brüt → 3900. Aynı ofis /
/// tanımsız lokasyon → satır yok. Çoklu tanımda ÇIKIŞ-ŞUBESİ eşleşen tercih (deterministik).
/// Önizleme == kayıt; uzatmada drop satırı 1'de kalır (tek seferlik — gün ölçeklemesinden bağımsız).
/// </summary>
[Collection("postgres")]
public sealed class DropUcretiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Drop", Soyad = "M" });
        return (m, v);
    }

    private static BookingInput Girdi(Guid m, Guid v, string? cikis, string? donus, decimal? dropUcreti = null) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m,
        CikisOfisi = cikis, DonusOfisi = donus, DropUcreti = dropUcreti
    };

    [Fact]
    public async Task Tanimdan_drop_ucreti_ve_manuel_override()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 01");
        await sp.GetRequiredService<DropTanimService>().CreateAsync(new DropTanimInput
        { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 500m });
        var rentals = sp.GetRequiredService<RentalService>();

        // Tanımdan: İstanbul çıkış → İzmir dönüş, 500 NET → 600 brüt → 3000+600=3600 (elle).
        var id = await rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR"));
        Assert.Equal(3600m, (await rentals.GetAsync(id))!.GenelToplam);
        var satir = Assert.Single(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id));
        Assert.Equal(500m, satir.NetTutar);
        Assert.Equal(600m, satir.Toplam);
        Assert.Equal(1m, satir.Miktar);                    // TEK SEFERLİK

        // Manuel override 750 NET → 900 brüt → 3900 (tanım 500 EZİLİR — operatör talimatı).
        var v2 = (await SeedAsync(sp, "34 DR 02")).v;
        var id2 = await rentals.CreateDirectAsync(Girdi(m, v2, "ISTANBUL", "IZMIR", dropUcreti: 750m));
        Assert.Equal(3900m, (await rentals.GetAsync(id2))!.GenelToplam);
    }

    [Fact]
    public async Task Ayni_ofis_veya_tanimsiz_lokasyon_ucretsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 03");
        await sp.GetRequiredService<DropTanimService>().CreateAsync(new DropTanimInput
        { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 500m });
        var rentals = sp.GetRequiredService<RentalService>();

        // Aynı ofis: drop koşulu yok → satır yok.
        var id1 = await rentals.CreateDirectAsync(Girdi(m, v, "IZMIR", "IZMIR"));
        Assert.Equal(3000m, (await rentals.GetAsync(id1))!.GenelToplam);

        // Farklı ofis ama dönüş lokasyonuna tanım yok → satır yok (sessiz — tanımsız rota ücretsiz).
        var v2 = (await SeedAsync(sp, "34 DR 04")).v;
        var id2 = await rentals.CreateDirectAsync(Girdi(m, v2, "IZMIR", "ANKARA"));
        Assert.Equal(3000m, (await rentals.GetAsync(id2))!.GenelToplam);
    }

    [Fact]
    public async Task Coklu_tanimda_cikis_subesi_tercihli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 05");
        var drops = sp.GetRequiredService<DropTanimService>();
        await drops.CreateAsync(new DropTanimInput { Lokasyon = "IZMIR", Sube = "ANKARA", Ucret = 400m });
        await drops.CreateAsync(new DropTanimInput { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 500m });
        var rentals = sp.GetRequiredService<RentalService>();

        // Çıkış ISTANBUL → İstanbul satırı tercih (500) → 3600.
        var id = await rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR"));
        Assert.Equal(3600m, (await rentals.GetAsync(id))!.GenelToplam);

        // Çıkış BURSA (hiçbiriyle eşleşmez) → deterministik ilk (Sube ordinal: ANKARA, 400) → 3480.
        var v2 = (await SeedAsync(sp, "34 DR 06")).v;
        var id2 = await rentals.CreateDirectAsync(Girdi(m, v2, "BURSA", "IZMIR"));
        Assert.Equal(3480m, (await rentals.GetAsync(id2))!.GenelToplam);   // 3000 + 480 (400 NET → 480)
    }

    [Fact]
    public async Task Sube_ozel_satir_son_sozdur() // adversarial B1 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 08");
        var drops = sp.GetRequiredService<DropTanimService>();
        // İstanbul çıkışı için AÇIKÇA ÜCRETSİZ (0) satır + Ankara için 400 — düzeltme öncesi 0'lık satır
        // elenip Ankara'nın 400'ü SESSİZCE tahsil ediliyordu; şube-özel satır artık SON SÖZ.
        await drops.CreateAsync(new DropTanimInput { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 0m });
        await drops.CreateAsync(new DropTanimInput { Lokasyon = "IZMIR", Sube = "ANKARA", Ucret = 400m });
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR"));
        Assert.Equal(3000m, (await rentals.GetAsync(id))!.GenelToplam);   // ücretsiz rota — satır yok

        // Özel satırı olmayan çıkış (BURSA) fallback'ten ücret alır (Ankara 400 → 480).
        var v2 = (await SeedAsync(sp, "34 DR 09")).v;
        var id2 = await rentals.CreateDirectAsync(Girdi(m, v2, "BURSA", "IZMIR"));
        Assert.Equal(3480m, (await rentals.GetAsync(id2))!.GenelToplam);
    }

    [Fact]
    public async Task Acik_sifir_override_muafiyettir() // adversarial B5 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 10");
        await sp.GetRequiredService<DropTanimService>().CreateAsync(new DropTanimInput
        { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 500m });
        var rentals = sp.GetRequiredService<RentalService>();

        // Açık 0 = muafiyet (null = otomatik): tanım 500 bastırılır → satır yok.
        var id = await rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR", dropUcreti: 0m));
        Assert.Equal(3000m, (await rentals.GetAsync(id))!.GenelToplam);
    }

    [Fact]
    public async Task Negatif_override_ve_faturali_degisiklik_reddedilir() // adversarial B3+B4 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 11");
        var rentals = sp.GetRequiredService<RentalService>();

        // B4: negatif override crafted POST'la bile reddedilir (create + update yolları).
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR", dropUcreti: -100m)));

        // B3: faturalanmış kirada DropUcreti değişikliği dondurulur (alan/satır ıraksaması kapalı).
        var id = await rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR", dropUcreti: 750m));
        await sp.GetRequiredService<RentACar.Application.Finance.InvoiceService>().CreateFromRentalAsync(id);
        var ex = await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => rentals.UpdateOpenAsync(id, new RentalUpdateInput { DropUcreti = 999m }));
        Assert.Contains("drop ücreti değiştirilemez", ex.Message);
    }

    [Fact]
    public async Task Onizleme_kayitla_bit_es_ve_uzatmada_tek_seferlik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 DR 07");
        await sp.GetRequiredService<DropTanimService>().CreateAsync(new DropTanimInput
        { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 500m });

        // Önizleme == kayıt: 3600.
        var onizleme = await sp.GetRequiredService<KiraHesapService>().HesaplaAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Bas, BitTar: Bas.AddDays(3), GunlukUcret: 1000m,
            FiyatTuru: null, Doviz: null, CikisOfisi: "ISTANBUL", EkHizmetler: [],
            MusteriId: m, DonusOfisi: "IZMIR"));
        Assert.True(onizleme.Ok);
        Assert.Equal(3600m, onizleme.GenelToplam);

        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Girdi(m, v, "ISTANBUL", "IZMIR"));
        Assert.Equal(3600m, (await rentals.GetAsync(id))!.GenelToplam);

        // Uzatma: baz 5×1000=5000; drop TEK SEFERLİK (Miktar=1 kalır) → 5000+600=5600 (elle).
        await rentals.ExtendAsync(id, Bas.AddDays(5));
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(5600m, c.GenelToplam);
        Assert.Equal(1m, (await sp.GetRequiredService<RentalAddOnService>().ListAsync(id)).Single().Miktar);
    }
}
