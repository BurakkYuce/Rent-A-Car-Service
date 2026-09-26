using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.RentalAddOns;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A3a — Sürücü ücretleri: genç sürücü + ek (2.) sürücü ücretleri SİSTEM RentalAddOn satırı
/// olarak (KURAL A: BaseGross/Tutar'a dokunulmaz — matrah-dışı ayrı kalem). BAĞIMSIZ ORACLE (elle):
/// manuel 1000/gün × 3g → Tutar 3000; grup: eşik 25 yaş, genç 100 NET/gün, ek sürücü 50 NET/gün;
/// 22 yaş + 2. sürücü → satırlar 300/60/360 ve 150/30/180 → GenelToplam 3000+360+180=3540.
/// Kenarlar: yaş ≥ eşik satır yok; doğum tarihi kayıtsız → ücret YOK (tahmin yapılmaz, not yalnız
/// önizlemede); FX kirada otomatik ücret atlanır (saf hesap); idempotency (çift çağrı/dönüşüm çift
/// ücret üretmez); önizleme == kayıt (aynı saf hesap + aynı kalem matematiği).
/// </summary>
[Collection("postgres")]
public sealed class SurucuUcretleriTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<Guid> GrupluAracAsync(IServiceProvider sp, string plaka)
    {
        if ((await sp.GetRequiredService<VehicleGroupService>().ListAsync()).All(g => g.Kod != "EKO"))
            await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
            {
                Kod = "EKO", Ad = "Ekonomik", GencSurucuYas = 25,
                GencSurucuUcretGunluk = 100m, EkSurucuUcretGunluk = 50m
            });
        return await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Grup = "EKO" });
    }

    private static Task<Guid> MusteriAsync(IServiceProvider sp, string ad, DateTimeOffset? dogum) =>
        sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = ad, Soyad = "SU", DogumTarihi = dogum });

    [Fact]
    public async Task Genc_ve_ek_surucu_ucretleri_addon_satiri_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 01");
        var genc = await MusteriAsync(sp, "Genc", Bas.AddYears(-22).AddDays(-1));   // 22 yaş < 25
        var ikinci = await MusteriAsync(sp, "Ikinci", null);
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = genc, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = ikinci });

        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(3000m, c.Tutar);              // KURAL A: baz Tutar'a dokunulmadı
        Assert.Equal(3540m, c.GenelToplam);        // 3000 + 360 (genç) + 180 (ek sürücü) — elle

        var satirlar = await sp.GetRequiredService<RentalAddOnService>().ListAsync(id);
        Assert.Equal(2, satirlar.Count);
        var gencSatir = satirlar.Single(s => s.Ad == "Genç sürücü ücreti");
        Assert.Equal(300m, gencSatir.NetTutar);    // 100 × 3 (elle)
        Assert.Equal(60m, gencSatir.KdvTutar);
        Assert.Equal(360m, gencSatir.Toplam);
        var ekSatir = satirlar.Single(s => s.Ad == "Ek sürücü ücreti");
        Assert.Equal(150m, ekSatir.NetTutar);      // 50 × 3
        Assert.Equal(180m, ekSatir.Toplam);

        // İDEMPOTENCY: ikinci uygulama satır/tutar üretmez (rez→kira dönüşümü çift-ücret çiti).
        await sp.GetRequiredService<FeeLineService>().ApplyContractFeesAsync(id);
        Assert.Equal(2, (await sp.GetRequiredService<RentalAddOnService>().ListAsync(id)).Count);
        Assert.Equal(3540m, (await rentals.GetAsync(id))!.GenelToplam);
    }

    [Fact]
    public async Task Esik_ustu_yas_ve_dogumsuz_musteri_ucretsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 02");
        var rentals = sp.GetRequiredService<RentalService>();

        // 30 yaş (eşik 25 üstü), 2. sürücü yok → hiç sistem satırı yok.
        var olgun = await MusteriAsync(sp, "Olgun", Bas.AddYears(-30));
        var id1 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = olgun, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m });
        Assert.Equal(3000m, (await rentals.GetAsync(id1))!.GenelToplam);
        Assert.Empty(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id1));

        // Doğum tarihi KAYITSIZ → ücret YOK (tahmin yapılmaz) — sessiz ücret sınıfı kapalı yönde.
        var v2 = await GrupluAracAsync(sp, "34 SU 03");
        var dogumsuz = await MusteriAsync(sp, "Dogumsuz", null);
        var id2 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = dogumsuz, VehicleId = v2, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m });
        Assert.Empty(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id2));
    }

    [Fact]
    public void Fx_kirada_otomatik_ucret_atlanir_notla() // saf hesap — kayıt ve önizleme aynı fonksiyon
    {
        var grup = new Domain.Entities.VehicleGroup
        { Kod = "EKO", Ad = "E", GencSurucuYas = 25, GencSurucuUcretGunluk = 100m, EkSurucuUcretGunluk = 50m };
        var notlar = new List<string>();
        var satirlar = FeeLineService.CalculatePure(grup, 3, Bas, Bas.AddYears(-20), true, "EUR", notlar);
        Assert.Empty(satirlar);
        Assert.Contains(notlar, n => n.Contains("Dövizli"));

        // TL'de aynı girdi 2 satır üretir (kontrast). TL EŞANLAMLILARI da TL sayılır — adversarial B1:
        // "₺"/"TRL" ayrı alias listesinde FX sanılıp ücret sessizce atlanıyordu (NormalizeKod tek kaynak).
        notlar.Clear();
        Assert.Equal(2, FeeLineService.CalculatePure(grup, 3, Bas, Bas.AddYears(-20), true, "TL", notlar).Count);
        Assert.Equal(2, FeeLineService.CalculatePure(grup, 3, Bas, Bas.AddYears(-20), true, "₺", notlar).Count);
        Assert.Equal(2, FeeLineService.CalculatePure(grup, 3, Bas, Bas.AddYears(-20), true, "trl", notlar).Count);
    }

    [Fact]
    public async Task Ikinci_surucu_degisikligi_ucretleri_senkronlar() // adversarial B2 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 06");
        var genc = await MusteriAsync(sp, "Genc", Bas.AddYears(-22).AddDays(-1));
        var ikinci = await MusteriAsync(sp, "Ikinci", null);
        var rentals = sp.GetRequiredService<RentalService>();
        var addOns = sp.GetRequiredService<RentalAddOnService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = genc, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = ikinci });
        Assert.Equal(3540m, (await rentals.GetAsync(id))!.GenelToplam);

        // 2. sürücü KALDIRILDI → ek sürücü satırı düşer (fazla ücret kalmaz): 3540 → 3360.
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { IkinciSurucuId = null });
        Assert.Equal(3360m, (await rentals.GetAsync(id))!.GenelToplam);
        Assert.Single(await addOns.ListAsync(id));

        // 2. sürücü GERİ EKLENDİ → satır gelir: 3360 → 3540 (eksik ücret kalmaz).
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { IkinciSurucuId = ikinci });
        Assert.Equal(3540m, (await rentals.GetAsync(id))!.GenelToplam);
        Assert.Equal(2, (await addOns.ListAsync(id)).Count);
    }

    [Fact]
    public async Task Uzatma_ucret_satirlarini_yeni_gune_olcekler() // adversarial B3 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 07");
        var genc = await MusteriAsync(sp, "Genc", Bas.AddYears(-22).AddDays(-1));
        var ikinci = await MusteriAsync(sp, "Ikinci", null);
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = genc, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = ikinci });
        await rentals.ExtendAsync(id, Bas.AddDays(5));

        // Elle: baz 5×1000=5000; ücretler NET/GÜN → 5 güne ölçeklenir: genç 500→600 brüt,
        // ek sürücü 250→300 brüt → GenelToplam 5900 (düzeltme öncesi 5540 — 360 eksik ücret).
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(5000m, c.Tutar);
        Assert.Equal(5900m, c.GenelToplam);
        var satirlar = await sp.GetRequiredService<RentalAddOnService>().ListAsync(id);
        Assert.Equal(2, satirlar.Count);
        Assert.All(satirlar, s => Assert.Equal(5m, s.Miktar));
    }

    [Fact]
    public async Task Sistem_tanimi_manuel_eklenemez() // adversarial B4 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 08");
        var genc = await MusteriAsync(sp, "Genc", Bas.AddYears(-22).AddDays(-1));
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = genc, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m });

        // Sistem tanımı artık mevcut (create yarattı) — manuel/matris yolundan EKLENEMEZ (çift ücret çiti).
        var sysTanim = (await sp.GetRequiredService<RentACar.Application.EkHizmetler.AddOnDefinitionService>()
            .ListAsync()).Single(t => t.Kod == FeeLineService.YoungDriverCode);
        var ex = await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<RentalAddOnService>().AddAsync(id, sysTanim.Id, 1m));
        Assert.Contains("manuel eklenemez", ex.Message);
    }

    [Fact]
    public async Task Rez_kira_donusumu_ucretleri_bir_kez_uygular()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 04");
        var genc = await MusteriAsync(sp, "Genc", Bas.AddYears(-22).AddDays(-1));
        var rez = sp.GetRequiredService<ReservationService>();

        var rid = await rez.CreateAsync(new BookingInput
        { MusteriId = genc, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m });
        Assert.Equal(3000m, (await rez.GetAsync(rid))!.Tutar);      // rezervasyonda ücret YOK (yalnız kirada)

        var kiraId = await rez.ConvertToRentalAsync(rid);
        var c = (await sp.GetRequiredService<RentalService>().GetAsync(kiraId))!;
        Assert.Equal(3360m, c.GenelToplam);                          // 3000 + 360 genç (2. sürücü dönüşümde yok)
        var satir = Assert.Single(await sp.GetRequiredService<RentalAddOnService>().ListAsync(kiraId));
        Assert.Equal(360m, satir.Toplam);
    }

    [Fact]
    public async Task Onizleme_kayitla_bit_es_ve_dogumsuz_notu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GrupluAracAsync(sp, "34 SU 05");
        var genc = await MusteriAsync(sp, "Genc", Bas.AddYears(-22).AddDays(-1));
        var ikinci = await MusteriAsync(sp, "Ikinci", null);
        var hesap = sp.GetRequiredService<RentalCalculationService>();

        var onizleme = await hesap.CalculateAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Bas, BitTar: Bas.AddDays(3), GunlukUcret: 1000m,
            FiyatTuru: null, Doviz: null, CikisOfisi: null, EkHizmetler: [],
            MusteriId: genc, IkinciSurucuId: ikinci));
        Assert.True(onizleme.Ok);
        Assert.Equal(3540m, onizleme.GenelToplam);                   // önizleme == kayıt (elle 3540)
        Assert.Equal(2, onizleme.EkKalemler.Count);

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = genc, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = ikinci });
        Assert.Equal(3540m, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.GenelToplam);

        // Doğum tarihi kayıtsız müşteri: önizleme NOT düşer (kayıt sessizce ücretsiz — tahmin yok).
        var dogumsuz = await MusteriAsync(sp, "Dogumsuz", null);
        var notlu = await hesap.CalculateAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Bas, BitTar: Bas.AddDays(3), GunlukUcret: 1000m,
            FiyatTuru: null, Doviz: null, CikisOfisi: null, EkHizmetler: [], MusteriId: dogumsuz));
        Assert.NotNull(notlu.Notlar);
        Assert.Contains(notlu.Notlar!, n => n.Contains("Doğum tarihi"));
    }
}
