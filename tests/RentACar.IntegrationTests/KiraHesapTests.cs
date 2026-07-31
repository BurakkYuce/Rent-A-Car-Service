using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira mega-form PR-B: KiraHesapService (GET /kiralar/hesapla arkası) — canlı önizleme GERÇEK motordan.
/// BAĞIMSIZ ORACLE: beklenen değerler elle (100×3=300 brüt → net 250 + KDV 50; "Günlük" 100→brüt 120×3=360;
/// tarife 3×200=600; ek 2×50 net %20 → 120). UI==motor garantisinin referans testleri.
/// </summary>
[Collection("postgres")]
public sealed class KiraHesapTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-5).AddHours(9);

    private static KiraHesapIstek Istek(
        Guid? vehicleId = null, decimal? ucret = 100m, string? fiyatTuru = null, string? doviz = null,
        int gun = 3, IReadOnlyList<KiraHesapEkHizmet>? ek = null, Guid? rentalId = null, string? cikisOfisi = null)
        => new(vehicleId, Bas, Bas.AddDays(gun), ucret, fiyatTuru, doviz, cikisOfisi, ek ?? [], rentalId);

    // ---------- Baz modlar (elle oracle) ----------
    [Fact]
    public async Task KdvDahilGunluk_100x3_300_net250_kdv50()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KiraHesapService>();
        var r = await svc.HesaplaAsync(Istek());
        Assert.True(r.Ok);
        Assert.Equal(3, r.Gun);
        Assert.Equal(300m, r.Tutar);      // 3 × 100 (brüt)
        Assert.Equal(250m, r.Net);        // 300 / 1.20
        Assert.Equal(50m, r.Kdv);         // 300 − 250
        Assert.Equal(300m, r.GenelToplam);
        Assert.Equal(300m, r.Kalan);      // tahsilatsız → genel toplam
    }

    [Fact]
    public async Task GunlukNet_100_brut120x3_360_iki_cagri_ayni()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KiraHesapService>();
        // Net modda çift gross-up riski (PriceAsync input mutasyonu) → her çağrı TAZE input kurmalı.
        var r1 = await svc.HesaplaAsync(Istek(fiyatTuru: "Günlük"));
        var r2 = await svc.HesaplaAsync(Istek(fiyatTuru: "Günlük"));
        Assert.Equal(360m, r1.Tutar);         // net 100 → brüt 120 × 3 gün (elle)
        Assert.Equal(120m, r1.GunlukUcret);   // brüte normalize edilmiş günlük
        Assert.Equal(r1.Tutar, r2.Tutar);     // ikinci çağrı ASLA 432 (çift gross-up) olmamalı
    }

    [Fact]
    public async Task ToplamNet_1000_brut1200_gun_bagimsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KiraHesapService>();
        var r = await svc.HesaplaAsync(Istek(ucret: 1000m, fiyatTuru: "Toplam", gun: 5));
        Assert.Equal(1200m, r.Tutar);     // NET toplam 1000 → brüt 1200 (gün sayısından bağımsız)
        Assert.Equal(5, r.Gun);
        Assert.Equal(240m, r.GunlukUcret); // türetilen günlük 1200/5
    }

    // ---------- Otomatik (tarife motoru) ----------
    [Fact]
    public async Task Otomatik_tarife_3x200_600()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Eko" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 01", Grup = "EKO" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-T", Ad = "Eko Tarife", AracGrupKod = "EKO",
            Gun1 = 200m, Gun2 = 200m, Gun3 = 200m, Gun4 = 200m, Gun5 = 200m, Gun6 = 200m, Gun7 = 200m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });
        var svc = sp.GetRequiredService<KiraHesapService>();
        // Manuel ücret gönderilse bile Otomatik'te yok sayılır (sunucu kuralı ile aynı).
        var r = await svc.HesaplaAsync(Istek(vehicleId: veh, ucret: 999m, fiyatTuru: "Otomatik"));
        Assert.True(r.Ok);
        Assert.Equal(600m, r.Tutar);      // 3 gün × 200 (elle — kural/iskonto seed'i yok)
        Assert.Equal(200m, r.GunlukUcret);
    }

    // Tarife yok AMA ücret girilmiş → panel artık HATA basmaz, girilen ücreti NET alıp KDV ekler.
    [Fact]
    public async Task Otomatik_tarife_yokken_girilen_ucrete_kdv_ekler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 02", Grup = "YOK" });
        var r = await sp.GetRequiredService<KiraHesapService>().HesaplaAsync(
            Istek(vehicleId: veh, ucret: 100m, fiyatTuru: "Otomatik"));
        Assert.True(r.Ok);
        Assert.Equal(120m, r.GunlukUcret);   // 100 × 1,20 (elle)
        Assert.Equal(360m, r.Tutar);         // 3 × 120 (elle)
    }

    // Tarife de ücret de yoksa panel HÂLÂ nazik hata basar (exception değil).
    [Fact]
    public async Task Otomatik_tarife_ve_ucret_yoksa_ok_false()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 03", Grup = "YOK" });
        var r = await sp.GetRequiredService<KiraHesapService>().HesaplaAsync(
            Istek(vehicleId: veh, ucret: 0m, fiyatTuru: "Otomatik"));
        Assert.False(r.Ok);
        Assert.Contains("tarife", r.Hata, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Ek hizmet kalemleri (AddAsync ile bit-eş) ----------
    [Fact]
    public async Task EkHizmet_2x50net_kdv20_120_genel_420()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var tanim = await sp.GetRequiredService<EkHizmetTanimService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "BKS", Ad = "Bebek Koltuğu", BirimUcret = 50m, KdvOrani = 0.20m });
        var svc = sp.GetRequiredService<KiraHesapService>();
        var r = await svc.HesaplaAsync(Istek(ek: [new KiraHesapEkHizmet(tanim, 2m)]));
        // ELLE: net 2×50=100; KDV %20 → 20; kalem brüt 120; genel = 300 + 120 = 420.
        Assert.Single(r.EkKalemler);
        Assert.Equal(100m, r.EkKalemler[0].Net);
        Assert.Equal(20m, r.EkKalemler[0].Kdv);
        Assert.Equal(120m, r.EkKalemler[0].Toplam);
        Assert.Equal(120m, r.EkHizmetToplam);
        Assert.Equal(420m, r.GenelToplam);
        Assert.Equal(420m, r.Kalan);
    }

    [Fact]
    public async Task EkHizmet_olmayan_tanim_sessiz_atlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KiraHesapService>();
        var r = await svc.HesaplaAsync(Istek(ek: [new KiraHesapEkHizmet(Guid.NewGuid(), 2m)]));
        Assert.True(r.Ok);
        Assert.Empty(r.EkKalemler);       // önizlemede düşer; kayıtta AddAsync temiz reddeder
        Assert.Equal(300m, r.GenelToplam);
    }

    // ---------- Döviz ----------
    [Fact]
    public async Task Doviz_usd_sabitkur40_tl_karsiligi_12000()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<SabitKurService>().UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true });
        var r = await sp.GetRequiredService<KiraHesapService>().HesaplaAsync(Istek(doviz: "USD"));
        Assert.Equal(300m, r.GenelToplam);     // kira dövizinde (USD)
        Assert.Equal(40m, r.Kur);
        Assert.Equal(12_000m, r.GenelToplamTl); // 300 × 40 (elle)
    }

    [Fact]
    public async Task Doviz_kur_yoksa_tl_karsiligi_null_ok_true()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // İZOLE kod "CHF": KurKayitlari PLATFORM tablosudur (RLS yok) — başka testlerin seed ettiği
        // USD/EUR kurları sızar (tam pakette USD=34.5 gelmişti). CHF'yi hiçbir test seed etmez.
        var r = await scope.ServiceProvider.GetRequiredService<KiraHesapService>().HesaplaAsync(Istek(doviz: "CHF"));
        Assert.True(r.Ok);                 // önizleme nazik — kayıt anında FX kur yoksa zaten temiz red var
        Assert.Null(r.Kur);
        Assert.Null(r.GenelToplamTl);
        Assert.Equal(300m, r.GenelToplam);
    }

    // ---------- RentalId bağlamı (Kalan) + kapsam ----------
    [Fact]
    public async Task RentalId_ile_tahsilat_dusulur_kalan_260()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Hesap", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 03" });
        var rentals = sp.GetRequiredService<RentalService>();
        var kira = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m }); // 360
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = cari, RentalId = kira, Tutar = 100m });

        var r = await sp.GetRequiredService<KiraHesapService>().HesaplaAsync(
            Istek(vehicleId: veh, ucret: 120m, rentalId: kira));
        Assert.Equal(360m, r.GenelToplam); // 3 × 120
        Assert.Equal(100m, r.Tahsilat);
        Assert.Equal(260m, r.Kalan);       // 360 − 100 (elle)
    }

    [Fact]
    public async Task RentalId_sube_kapsami_disi_operator_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid kira, veh;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var cari = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Ankara", Soyad = "Musteri" });
            veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 04" });
            kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m, CikisOfisi = "Ankara" });
        }
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        await Assert.ThrowsAsync<ValidationException>(() =>
            op.ServiceProvider.GetRequiredService<KiraHesapService>()
              .HesaplaAsync(Istek(vehicleId: veh, ucret: 120m, rentalId: kira)));
    }

    // ---------- Guard'lar + yan-etkisizlik ----------
    [Fact]
    public async Task BitTar_kucuk_esit_ok_false()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<KiraHesapService>();
        var r = await svc.HesaplaAsync(new KiraHesapIstek(null, Bas, Bas, 100m, null, null, null, []));
        Assert.False(r.Ok);
        Assert.Contains("Bitiş", r.Hata);
    }

    [Fact]
    public async Task Tasma_girdileri_ok_false_500_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<KiraHesapService>();

        // Devasa günlük ücret → nazik red (önceden gün×ücret çarpımı OverflowException → 500 idi)
        var r1 = await svc.HesaplaAsync(Istek(ucret: decimal.MaxValue));
        Assert.False(r1.Ok);
        // Negatif ücret → nazik red (negatif Tutar önizlemesi yanıltıcıydı)
        var r2 = await svc.HesaplaAsync(Istek(ucret: -100m));
        Assert.False(r2.Ok);
        // Devasa ek hizmet miktarı → nazik red (round(birim×miktar) taşması)
        var tanim = await sp.GetRequiredService<EkHizmetTanimService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "TSM", Ad = "Taşma", BirimUcret = 50m, KdvOrani = 0.20m });
        var r3 = await svc.HesaplaAsync(Istek(ek: [new KiraHesapEkHizmet(tanim, decimal.MaxValue)]));
        Assert.False(r3.Ok);

        // Simetrik guard create yolunda da (AddAsync) — taşma yerine temiz ValidationException
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Taşma", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 06" });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m });
        await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentACar.Application.RentalAddOns.RentalAddOnService>()
              .AddAsync(kira, tanim, decimal.MaxValue));
    }

    [Fact]
    public async Task Hesapla_persist_etmez_mevcut_kirayi_degistirmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Sabit", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KH 05" });
        var rentals = sp.GetRequiredService<RentalService>();
        var kira = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m });

        var svc = sp.GetRequiredService<KiraHesapService>();
        await svc.HesaplaAsync(Istek(vehicleId: veh, ucret: 999m, fiyatTuru: "Günlük"));
        await svc.HesaplaAsync(Istek(vehicleId: veh, ucret: 5m, gun: 30, rentalId: kira));

        Assert.Single(await rentals.ListAsync());                 // yeni satır yok
        var c = await rentals.GetAsync(kira);
        Assert.Equal(360m, c!.Tutar);                             // mevcut kira DOKUNULMAMIŞ (3×120 elle)
        Assert.Equal(120m, c.GunlukUcret);
    }
}
