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
    private static readonly DateTimeOffset T0 = TestZaman.Now().AddDays(-15);

    private static async Task<(Guid kira, Guid cari)> RentalAsync(
        IServiceProvider sp, string title, string plate, string? office = null, string? source = null)
    {
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = title });
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, BasTar = T0, BitTar = T0.AddDays(3),
            GunlukUcret = 1000m, CikisOfisi = office, Kaynak = source
        });
        return (kira: rental, cari: account);
    }

    private static Task<Guid> DefinitionAsync(IServiceProvider sp, string code, string name, decimal fee)
        => sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(new EkHizmetTanimInput
        { Kod = code, Ad = name, BirimUcret = fee, KdvOrani = 0.20m });

    [Fact]
    public async Task Detay_satirlari_JOIN_alanlariyla_ve_ELLE_HESAPLANAN_tutarlarla_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (rental, _) = await RentalAsync(sp, "Alfa A.Ş.", "34 EH 01", "Merkez Ofis", "Web");
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 150m);
        var staff = await sp.GetRequiredService<PersonnelService>()
            .CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ayşe", Soyad = "Yılmaz" });

        // ELLE: 2 × 150 = 300 net, %20 → 60 KDV, 360 brüt.
        await sp.GetRequiredService<RentalAddOnService>()
            .AddAsync(rental, gps, 2m, staffId: staff);

        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetAddOnDetailAsync());
        Assert.Equal("Navigasyon", row.Ad);
        Assert.Equal(2m, row.Miktar);
        Assert.Equal(150m, row.BirimNetFiyat);
        Assert.Equal(300m, row.Net);
        Assert.Equal(60m, row.Kdv);
        Assert.Equal(360m, row.Brut);

        // JOIN alanları
        Assert.Equal(rental, row.RentalId);
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

        var (k1, _) = await RentalAsync(sp, "Alfa", "34 EH 10");
        var (k2, _) = await RentalAsync(sp, "Beta", "34 EH 11");
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
        var seat = await DefinitionAsync(sp, "BBK", "Bebek Koltuğu", 50m);

        // ELLE: k1'e 1×GPS (100/20/120) ve 2×koltuk (100/20/120); k2'ye 1×GPS (100/20/120).
        await addOns.AddAsync(k1, gps, 1m);
        await addOns.AddAsync(k1, seat, 2m);
        await addOns.AddAsync(k2, gps, 1m);

        var report = sp.GetRequiredService<ReportService>();
        var summary = await report.GetAddOnReportAsync();
        var detail = await report.GetAddOnDetailAsync();

        // Özet: 2 farklı ad, toplam net 300, KDV 60, brüt 360, 2 kirada.
        Assert.Equal(2, summary.Satirlar.Count);
        Assert.Equal(300m, summary.ToplamNet);
        Assert.Equal(60m, summary.ToplamKdv);
        Assert.Equal(360m, summary.ToplamBrut);
        Assert.Equal(2, summary.KiraAdet);

        // Detay: 3 satır — ve toplamları ÖZETLE BİREBİR aynı olmalı.
        Assert.Equal(3, detail.Count);
        Assert.Equal(summary.ToplamNet, detail.Sum(x => x.Net));
        Assert.Equal(summary.ToplamKdv, detail.Sum(x => x.Kdv));
        Assert.Equal(summary.ToplamBrut, detail.Sum(x => x.Brut));
        Assert.Equal(summary.KiraAdet, detail.Select(x => x.RentalId).Distinct().Count());
    }

    [Fact]
    public async Task Ilk_tahsilat_KIRANIN_en_erken_tahsilatidir_ters_kayit_sayilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (rental, account) = await RentalAsync(sp, "Gama", "34 EH 20");
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, gps, 1m);

        var cash = sp.GetRequiredService<CashService>();
        // Ters kayıtlı bir tahsilat "ilk tahsilat" sayılmamalı.
        var cancelled = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 900m, RentalId = rental });
        await cash.ReverseAsync(cancelled);
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 400m, RentalId = rental });
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 500m, RentalId = rental });

        var row = Assert.Single(await sp.GetRequiredService<ReportService>().GetAddOnDetailAsync());
        // Geçerli tahsilatların en erkeni 400 (900 ters kayıtlı, sayılmaz).
        Assert.Equal(400m, row.IlkTahsilat);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();
        var addOns = sp.GetRequiredService<RentalAddOnService>();

        var (k1, _) = await RentalAsync(sp, "Alfa Lojistik", "34 AL 01", "Merkez Ofis", "Web");
        var (k2, _) = await RentalAsync(sp, "Beta Turizm", "06 BT 02", "Şube2 Ofis", "Acente");
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
        var seat = await DefinitionAsync(sp, "BBK", "Bebek Koltuğu", 50m);
        var p1 = await sp.GetRequiredService<PersonnelService>()
            .CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ayşe", Soyad = "Yılmaz" });

        await addOns.AddAsync(k1, gps, 1m, staffId: p1);
        await addOns.AddAsync(k1, seat, 1m);
        await addOns.AddAsync(k2, gps, 1m);

        Assert.Equal(3, (await report.GetAddOnDetailAsync()).Count);
        Assert.Equal(3, (await report.GetAddOnDetailAsync(new EkHizmetDetayFilter())).Count);

        // Metin: hizmet adı / plaka (boşluklu) / müşteri
        Assert.Equal(2, (await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Ara = "navigasyon" })).Count);
        Assert.Equal(2, (await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Ara = "34 AL" })).Count);
        Assert.Single(await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Ara = "Beta" }));

        // Personel
        Assert.Equal("Ayşe Yılmaz", Assert.Single(await report.GetAddOnDetailAsync(
            new EkHizmetDetayFilter { PersonelId = p1 })).SatanPersonel);

        // Ofis + rezervasyon kaynağı
        Assert.Equal(2, (await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Ofis = "Merkez Ofis" })).Count);
        Assert.Single(await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Ofis = "Şube2 Ofis" }));

        // Tarih: kalem eklenme tarihi bugün → geniş aralık hepsini verir, geçmişe kapanan hiçbirini.
        var today = TestZaman.Now();
        Assert.Equal(3, (await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Bas = today.AddDays(-1) })).Count);
        Assert.Empty(await report.GetAddOnDetailAsync(new EkHizmetDetayFilter { Bit = today.AddDays(-1) }));
    }

    [Fact]
    public async Task Iptal_edilen_kiranin_kalemleri_HER_IKI_gorunumde_de_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (rental, _) = await RentalAsync(sp, "Delta", "34 EH 30");
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, gps, 1m);

        var report = sp.GetRequiredService<ReportService>();
        Assert.Single(await report.GetAddOnDetailAsync());
        Assert.Equal(120m, (await report.GetAddOnReportAsync()).ToplamBrut);

        await sp.GetRequiredService<RentalService>().CancelAsync(rental);

        // İki görünüm de İptal kirayı dışlamalı — ayrışırlarsa raporlar birbirini tutmaz.
        Assert.Empty(await report.GetAddOnDetailAsync());
        Assert.Equal(0m, (await report.GetAddOnReportAsync()).ToplamBrut);
    }

    [Fact]
    public async Task Detay_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var (rental, _) = await RentalAsync(sp, "Gizli", "34 GZ 01");
            var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
            await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, gps, 1m);
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>().GetAddOnDetailAsync());
    }
}
