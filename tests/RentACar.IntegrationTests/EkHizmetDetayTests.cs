using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Finance;
using RentACar.Application.Personnel;
using RentACar.Application.RentalAddOns;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-78 — Ek hizmet raporu satır-bazlı detay + satan personel atfı.
///
/// <para><b>Regresyon çiti (bu fazın en önemli testi):</b> mevcut AD-BAZLI ÖZET raporu
/// <c>GetEkHizmetRaporuAsync</c> DEĞİŞMEMELİ. Aşağıda aynı veri kümesiyle özet toplamları elle
/// hesaplanan sabitlerle karşılaştırılıyor ve detay listesinin toplamıyla da tutması isteniyor —
/// iki görünüm birbirinden ayrışamaz.</para>
///
/// <para>Bağımsız oracle: kalem tutarları testte elle hesaplanır (ör. "2 × 150 = 300 net,
/// %20 KDV → 60 KDV, 360 brüt").</para>
/// </summary>
[Collection("postgres")]
public sealed class EkHizmetDetayTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Simdi().AddDays(-15);

    private static async Task<(Guid kira, Guid cari)> KiraAsync(
        IServiceProvider sp, string unvan, string plaka, string? ofis = null, string? kaynak = null)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = unvan });
        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = T0, BitTar = T0.AddDays(3),
            GunlukUcret = 1000m, CikisOfisi = ofis, Kaynak = kaynak
        });
        return (kira, cari);
    }

    private static Task<Guid> TanimAsync(IServiceProvider sp, string kod, string ad, decimal ucret)
        => sp.GetRequiredService<EkHizmetTanimService>().CreateAsync(new EkHizmetTanimInput
        { Kod = kod, Ad = ad, BirimUcret = ucret, KdvOrani = 0.20m });

    [Fact]
    public async Task Detay_satirlari_JOIN_alanlariyla_ve_ELLE_HESAPLANAN_tutarlarla_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, _) = await KiraAsync(sp, "Alfa A.Ş.", "34 EH 01", "Merkez Ofis", "Web");
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 150m);
        var personel = await sp.GetRequiredService<PersonelService>()
            .CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ayşe", Soyad = "Yılmaz" });

        // ELLE: 2 × 150 = 300 net, %20 → 60 KDV, 360 brüt.
        await sp.GetRequiredService<RentalAddOnService>()
            .AddAsync(kira, gps, 2m, personelId: personel);

        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetEkHizmetDetayAsync());
        Assert.Equal("Navigasyon", row.Ad);
        Assert.Equal(2m, row.Miktar);
        Assert.Equal(150m, row.BirimNetFiyat);
        Assert.Equal(300m, row.Net);
        Assert.Equal(60m, row.Kdv);
        Assert.Equal(360m, row.Brut);

        // JOIN alanları
        Assert.Equal(kira, row.RentalId);
        Assert.False(string.IsNullOrWhiteSpace(row.SozlesmeNo));
        Assert.Equal("34EH01", row.Plaka);              // plaka DB'de normalize
        Assert.Equal("Alfa A.Ş.", row.MusteriAd);
        Assert.Equal("Merkez Ofis", row.CikisOfisi);
        Assert.Equal("Ayşe Yılmaz", row.SatanPersonel);
        Assert.False(row.SistemKalemi);
        Assert.Null(row.IlkTahsilat);                    // henüz tahsilat yok
    }

    [Fact]
    public async Task Ozet_rapor_REGRESYONSUZ_ve_detayla_TUTARLI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var addOns = sp.GetRequiredService<RentalAddOnService>();

        var (k1, _) = await KiraAsync(sp, "Alfa", "34 EH 10");
        var (k2, _) = await KiraAsync(sp, "Beta", "34 EH 11");
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
        var koltuk = await TanimAsync(sp, "BBK", "Bebek Koltuğu", 50m);

        // ELLE: k1'e 1×GPS (100/20/120) ve 2×koltuk (100/20/120); k2'ye 1×GPS (100/20/120).
        await addOns.AddAsync(k1, gps, 1m);
        await addOns.AddAsync(k1, koltuk, 2m);
        await addOns.AddAsync(k2, gps, 1m);

        var rapor = sp.GetRequiredService<ReportService>();
        var ozet = await rapor.GetEkHizmetRaporuAsync();
        var detay = await rapor.GetEkHizmetDetayAsync();

        // Özet: 2 farklı ad, toplam net 300, KDV 60, brüt 360, 2 kirada.
        Assert.Equal(2, ozet.Satirlar.Count);
        Assert.Equal(300m, ozet.ToplamNet);
        Assert.Equal(60m, ozet.ToplamKdv);
        Assert.Equal(360m, ozet.ToplamBrut);
        Assert.Equal(2, ozet.KiraAdet);

        // Detay: 3 satır — ve toplamları ÖZETLE BİREBİR aynı olmalı.
        Assert.Equal(3, detay.Count);
        Assert.Equal(ozet.ToplamNet, detay.Sum(x => x.Net));
        Assert.Equal(ozet.ToplamKdv, detay.Sum(x => x.Kdv));
        Assert.Equal(ozet.ToplamBrut, detay.Sum(x => x.Brut));
        Assert.Equal(ozet.KiraAdet, detay.Select(x => x.RentalId).Distinct().Count());
    }

    [Fact]
    public async Task Ilk_tahsilat_KIRANIN_en_erken_tahsilatidir_ters_kayit_sayilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, cari) = await KiraAsync(sp, "Gama", "34 EH 20");
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(kira, gps, 1m);

        var cash = sp.GetRequiredService<CashService>();
        // Ters kayıtlı bir tahsilat "ilk tahsilat" sayılmamalı.
        var iptalEdilen = await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 900m, RentalId = kira });
        await cash.ReverseAsync(iptalEdilen);
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 400m, RentalId = kira });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 500m, RentalId = kira });

        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetEkHizmetDetayAsync());
        // Geçerli tahsilatların en erkeni 400 (900 ters kayıtlı, sayılmaz).
        Assert.Equal(400m, row.IlkTahsilat);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();
        var addOns = sp.GetRequiredService<RentalAddOnService>();

        var (k1, _) = await KiraAsync(sp, "Alfa Lojistik", "34 AL 01", "Merkez Ofis", "Web");
        var (k2, _) = await KiraAsync(sp, "Beta Turizm", "06 BT 02", "Şube2 Ofis", "Acente");
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
        var koltuk = await TanimAsync(sp, "BBK", "Bebek Koltuğu", 50m);
        var p1 = await sp.GetRequiredService<PersonelService>()
            .CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ayşe", Soyad = "Yılmaz" });

        await addOns.AddAsync(k1, gps, 1m, personelId: p1);
        await addOns.AddAsync(k1, koltuk, 1m);
        await addOns.AddAsync(k2, gps, 1m);

        Assert.Equal(3, (await rapor.GetEkHizmetDetayAsync()).Count);
        Assert.Equal(3, (await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter())).Count);

        // Metin: hizmet adı / plaka (boşluklu) / müşteri
        Assert.Equal(2, (await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Ara = "navigasyon" })).Count);
        Assert.Equal(2, (await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Ara = "34 AL" })).Count);
        Assert.Single(await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Ara = "Beta" }));

        // Personel
        Assert.Equal("Ayşe Yılmaz", Assert.Single(await rapor.GetEkHizmetDetayAsync(
            new EkHizmetDetayFilter { PersonelId = p1 })).SatanPersonel);

        // Ofis + rezervasyon kaynağı
        Assert.Equal(2, (await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Ofis = "Merkez Ofis" })).Count);
        Assert.Single(await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Ofis = "Şube2 Ofis" }));

        // Tarih: kalem eklenme tarihi bugün → geniş aralık hepsini verir, geçmişe kapanan hiçbirini.
        var bugun = TestZaman.Simdi();
        Assert.Equal(3, (await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Bas = bugun.AddDays(-1) })).Count);
        Assert.Empty(await rapor.GetEkHizmetDetayAsync(new EkHizmetDetayFilter { Bit = bugun.AddDays(-1) }));
    }

    [Fact]
    public async Task Iptal_edilen_kiranin_kalemleri_HER_IKI_gorunumde_de_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, _) = await KiraAsync(sp, "Delta", "34 EH 30");
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(kira, gps, 1m);

        var rapor = sp.GetRequiredService<ReportService>();
        Assert.Single(await rapor.GetEkHizmetDetayAsync());
        Assert.Equal(120m, (await rapor.GetEkHizmetRaporuAsync()).ToplamBrut);

        await sp.GetRequiredService<RentalService>().CancelAsync(kira);

        // İki görünüm de İptal kirayı dışlamalı — ayrışırlarsa raporlar birbirini tutmaz.
        Assert.Empty(await rapor.GetEkHizmetDetayAsync());
        Assert.Equal(0m, (await rapor.GetEkHizmetRaporuAsync()).ToplamBrut);
    }

    [Fact]
    public async Task Detay_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var (kira, _) = await KiraAsync(sp, "Gizli", "34 GZ 01");
            var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
            await sp.GetRequiredService<RentalAddOnService>().AddAsync(kira, gps, 1m);
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>().GetEkHizmetDetayAsync());
    }
}
