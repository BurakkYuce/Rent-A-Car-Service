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
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<Guid> GroupedVehicleAsync(IServiceProvider sp, string plate)
    {
        if ((await sp.GetRequiredService<VehicleGroupService>().ListAsync()).All(g => g.Kod != "EKO"))
            await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
            {
                Kod = "EKO", Ad = "Ekonomik", GencSurucuYas = 25,
                GencSurucuUcretGunluk = 100m, EkSurucuUcretGunluk = 50m
            });
        return await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Grup = "EKO" });
    }

    private static Task<Guid> CustomerAsync(IServiceProvider sp, string name, DateTimeOffset? birth) =>
        sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = name, Soyad = "SU", DogumTarihi = birth });

    [Fact]
    public async Task Genc_ve_ek_surucu_ucretleri_addon_satiri_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GroupedVehicleAsync(sp, "34 SU 01");
        var young = await CustomerAsync(sp, "Genc", Start.AddYears(-22).AddDays(-1));   // 22 yaş < 25
        var second = await CustomerAsync(sp, "Ikinci", null);
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = young, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = second });

        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(3000m, c.Tutar);              // KURAL A: baz Tutar'a dokunulmadı
        Assert.Equal(3540m, c.GenelToplam);        // 3000 + 360 (genç) + 180 (ek sürücü) — elle

        var rows = await sp.GetRequiredService<RentalAddOnService>().ListAsync(id);
        Assert.Equal(2, rows.Count);
        var youngRow = rows.Single(s => s.Ad == "Genç sürücü ücreti");
        Assert.Equal(300m, youngRow.NetTutar);    // 100 × 3 (elle)
        Assert.Equal(60m, youngRow.KdvTutar);
        Assert.Equal(360m, youngRow.Toplam);
        var extraRow = rows.Single(s => s.Ad == "Ek sürücü ücreti");
        Assert.Equal(150m, extraRow.NetTutar);      // 50 × 3
        Assert.Equal(180m, extraRow.Toplam);

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
        var v = await GroupedVehicleAsync(sp, "34 SU 02");
        var rentals = sp.GetRequiredService<RentalService>();

        // 30 yaş (eşik 25 üstü), 2. sürücü yok → hiç sistem satırı yok.
        var mature = await CustomerAsync(sp, "Olgun", Start.AddYears(-30));
        var id1 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = mature, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });
        Assert.Equal(3000m, (await rentals.GetAsync(id1))!.GenelToplam);
        Assert.Empty(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id1));

        // Doğum tarihi KAYITSIZ → ücret YOK (tahmin yapılmaz) — sessiz ücret sınıfı kapalı yönde.
        var v2 = await GroupedVehicleAsync(sp, "34 SU 03");
        var withoutBirthDate = await CustomerAsync(sp, "Dogumsuz", null);
        var id2 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = withoutBirthDate, VehicleId = v2, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });
        Assert.Empty(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id2));
    }

    [Fact]
    public void Fx_kirada_otomatik_ucret_atlanir_notla() // saf hesap — kayıt ve önizleme aynı fonksiyon
    {
        var group = new Domain.Entities.VehicleGroup
        { Kod = "EKO", Ad = "E", GencSurucuYas = 25, GencSurucuUcretGunluk = 100m, EkSurucuUcretGunluk = 50m };
        var notes = new List<string>();
        var rows = FeeLineService.CalculatePure(group, 3, Start, Start.AddYears(-20), true, "EUR", notes);
        Assert.Empty(rows);
        Assert.Contains(notes, n => n.Contains("Dövizli"));

        // TL'de aynı girdi 2 satır üretir (kontrast). TL EŞANLAMLILARI da TL sayılır — adversarial B1:
        // "₺"/"TRL" ayrı alias listesinde FX sanılıp ücret sessizce atlanıyordu (NormalizeKod tek kaynak).
        notes.Clear();
        Assert.Equal(2, FeeLineService.CalculatePure(group, 3, Start, Start.AddYears(-20), true, "TL", notes).Count);
        Assert.Equal(2, FeeLineService.CalculatePure(group, 3, Start, Start.AddYears(-20), true, "₺", notes).Count);
        Assert.Equal(2, FeeLineService.CalculatePure(group, 3, Start, Start.AddYears(-20), true, "trl", notes).Count);
    }

    [Fact]
    public async Task Ikinci_surucu_degisikligi_ucretleri_senkronlar() // adversarial B2 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GroupedVehicleAsync(sp, "34 SU 06");
        var young = await CustomerAsync(sp, "Genc", Start.AddYears(-22).AddDays(-1));
        var second = await CustomerAsync(sp, "Ikinci", null);
        var rentals = sp.GetRequiredService<RentalService>();
        var addOns = sp.GetRequiredService<RentalAddOnService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = young, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = second });
        Assert.Equal(3540m, (await rentals.GetAsync(id))!.GenelToplam);

        // 2. sürücü KALDIRILDI → ek sürücü satırı düşer (fazla ücret kalmaz): 3540 → 3360.
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { IkinciSurucuId = null });
        Assert.Equal(3360m, (await rentals.GetAsync(id))!.GenelToplam);
        Assert.Single(await addOns.ListAsync(id));

        // 2. sürücü GERİ EKLENDİ → satır gelir: 3360 → 3540 (eksik ücret kalmaz).
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { IkinciSurucuId = second });
        Assert.Equal(3540m, (await rentals.GetAsync(id))!.GenelToplam);
        Assert.Equal(2, (await addOns.ListAsync(id)).Count);
    }

    [Fact]
    public async Task Uzatma_ucret_satirlarini_yeni_gune_olcekler() // adversarial B3 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GroupedVehicleAsync(sp, "34 SU 07");
        var young = await CustomerAsync(sp, "Genc", Start.AddYears(-22).AddDays(-1));
        var second = await CustomerAsync(sp, "Ikinci", null);
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = young, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = second });
        await rentals.ExtendAsync(id, Start.AddDays(5));

        // Elle: baz 5×1000=5000; ücretler NET/GÜN → 5 güne ölçeklenir: genç 500→600 brüt,
        // ek sürücü 250→300 brüt → GenelToplam 5900 (düzeltme öncesi 5540 — 360 eksik ücret).
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(5000m, c.Tutar);
        Assert.Equal(5900m, c.GenelToplam);
        var rows = await sp.GetRequiredService<RentalAddOnService>().ListAsync(id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, s => Assert.Equal(5m, s.Miktar));
    }

    [Fact]
    public async Task Sistem_tanimi_manuel_eklenemez() // adversarial B4 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GroupedVehicleAsync(sp, "34 SU 08");
        var young = await CustomerAsync(sp, "Genc", Start.AddYears(-22).AddDays(-1));
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = young, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });

        // Sistem tanımı artık mevcut (create yarattı) — manuel/matris yolundan EKLENEMEZ (çift ücret çiti).
        var sysDefinition = (await sp.GetRequiredService<RentACar.Application.EkHizmetler.AddOnDefinitionService>()
            .ListAsync()).Single(t => t.Kod == FeeLineService.YoungDriverCode);
        var ex = await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<RentalAddOnService>().AddAsync(id, sysDefinition.Id, 1m));
        Assert.Contains("manuel eklenemez", ex.Message);
    }

    [Fact]
    public async Task Rez_kira_donusumu_ucretleri_bir_kez_uygular()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GroupedVehicleAsync(sp, "34 SU 04");
        var young = await CustomerAsync(sp, "Genc", Start.AddYears(-22).AddDays(-1));
        var res = sp.GetRequiredService<ReservationService>();

        var rid = await res.CreateAsync(new BookingInput
        { MusteriId = young, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });
        Assert.Equal(3000m, (await res.GetAsync(rid))!.Tutar);      // rezervasyonda ücret YOK (yalnız kirada)

        var rentalId = await res.ConvertToRentalAsync(rid);
        var c = (await sp.GetRequiredService<RentalService>().GetAsync(rentalId))!;
        Assert.Equal(3360m, c.GenelToplam);                          // 3000 + 360 genç (2. sürücü dönüşümde yok)
        var row = Assert.Single(await sp.GetRequiredService<RentalAddOnService>().ListAsync(rentalId));
        Assert.Equal(360m, row.Toplam);
    }

    [Fact]
    public async Task Onizleme_kayitla_bit_es_ve_dogumsuz_notu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await GroupedVehicleAsync(sp, "34 SU 05");
        var young = await CustomerAsync(sp, "Genc", Start.AddYears(-22).AddDays(-1));
        var second = await CustomerAsync(sp, "Ikinci", null);
        var account = sp.GetRequiredService<RentalCalculationService>();

        var preview = await account.CalculateAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Start, BitTar: Start.AddDays(3), GunlukUcret: 1000m,
            FiyatTuru: null, Doviz: null, CikisOfisi: null, EkHizmetler: [],
            MusteriId: young, IkinciSurucuId: second));
        Assert.True(preview.Ok);
        Assert.Equal(3540m, preview.GenelToplam);                   // önizleme == kayıt (elle 3540)
        Assert.Equal(2, preview.EkKalemler.Count);

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = young, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m, IkinciSurucuId = second });
        Assert.Equal(3540m, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.GenelToplam);

        // Doğum tarihi kayıtsız müşteri: önizleme NOT düşer (kayıt sessizce ücretsiz — tahmin yok).
        var withoutBirthDate = await CustomerAsync(sp, "Dogumsuz", null);
        var withNote = await account.CalculateAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Start, BitTar: Start.AddDays(3), GunlukUcret: 1000m,
            FiyatTuru: null, Doviz: null, CikisOfisi: null, EkHizmetler: [], MusteriId: withoutBirthDate));
        Assert.NotNull(withNote.Notlar);
        Assert.Contains(withNote.Notlar!, n => n.Contains("Doğum tarihi"));
    }
}
