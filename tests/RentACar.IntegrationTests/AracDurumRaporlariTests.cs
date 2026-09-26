using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.RentalAddOns;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-12 — Araç durum/günlük durum/gelir-gider raporları derinliği.
///
/// <para><b>Bölüm A</b> — <c>arac_durum_takip</c> ARAÇ kırılımı: canlıda satır ARAÇ'tır (aralıkta
/// toplam Dolu/Bakım/Baf/Boş GÜN); bizde yalnız GÜN kırılımı vardı. Kovalar öncelikli sayılır
/// (Dolu &gt; Bakım &gt; Baf &gt; Boş) → dördünün toplamı daima aralık gün sayısıdır.</para>
///
/// <para><b>Bölüm B</b> — <c>arac_gunluk_durum</c>: seçilen günde aktif kiraların araç-bazlı günlük
/// gelir kesiti. <b>PROJEKSİYON</b>; deftere yazmaz. Aşağıdaki
/// <see cref="Gunluk_durum_PROJEKSIYONDUR_defter_PL_ini_DEGISTIRMEZ"/> testi bunun kırılgan
/// kilididir: "P&amp;L yalnız defterden" kuralı ihlal edilirse kırmızı yanar.</para>
///
/// <para><b>Bölüm C</b> (KARARLAR.md "Seçenek B") — ek hizmet raporuna ARAÇ bazlı pivot modu.
/// Üç rapor (Karlılık / Filo Analiz / Ek Hizmet) BİRLEŞTİRİLMEDİ. Pivot yeni para üretmez:
/// genel toplamı ad-bazlı özetin brütüne EŞİTTİR (parite kilidi).</para>
///
/// <para><b>Bağımsız oracle:</b> tüm beklenen sayılar aşağıdaki senaryodan ELLE türetilmiştir
/// (kova gün sayıları takvimden, tutarlar "3 gün × 100 = 300" gibi elle çarpımdan); rapor
/// kodundan ÜRETİLMEMİŞTİR.</para>
/// </summary>
[Collection("postgres")]
public sealed class AracDurumRaporlariTests(PostgresFixture fx)
{
    /// <summary>Şimdiye göre çıpalanmış gün-tabanı (sabit takvim tarihi bir yıl sonra
    /// TarihPolitikasi'nın geçmiş sınırına çarpardı). Gece yarısı UTC.</summary>
    private static DateTimeOffset Taban => new(TestZaman.Simdi().UtcDateTime.Date, TimeSpan.Zero);

    private static Task<Guid> AracAsync(IServiceProvider sp, string plaka,
        string? sube = null, string? grup = null, string? sipp = null, string? sahip = null)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = plaka, Sube = sube, Grup = grup, Sipp = sipp, AracSahibi = sahip });

    private static Task<Guid> CariAsync(IServiceProvider sp, string unvan)
        => sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = unvan });

    private static Task<Guid> KiraAsync(IServiceProvider sp, Guid cari, Guid arac,
        DateTimeOffset bas, DateTimeOffset bit, decimal gunluk, string? ofis = null)
        => sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = bas, BitTar = bit,
            GunlukUcret = gunluk, CikisOfisi = ofis
        });

    private static Task<Guid> TanimAsync(IServiceProvider sp, string kod, string ad, decimal ucret)
        => sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(new EkHizmetTanimInput
        { Kod = kod, Ad = ad, BirimUcret = ucret, KdvOrani = 0.20m });

    // ==================== Bölüm A — araç kırılımı ====================

    [Fact]
    public async Task Arac_bazli_kovalar_ELLE_SAYILAN_GUNLERLE_birebir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var arac = await AracAsync(sp, "34 DK 01");
        var cari = await CariAsync(sp, "Durum A.Ş.");

        // ELLE KURULAN TAKVİM — aralık T+0 … T+9 (10 gün):
        //   kira   T+1 → T+3  → dolu günler {1,2,3}          = 3
        //   servis T+5 → T+5  → bakım günü  {5}              = 1
        //   BAF    T+7 → T+8  → baf günleri {7,8}            = 2
        //   kalan  {0,4,6,9}  → boş                          = 4
        await KiraAsync(sp, cari, arac, Taban.AddDays(1), Taban.AddDays(3), 100m);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SRV-DK1", VehicleId = arac, Durum = ServiceStatus.Tamamlandi,
                GirisTarihi = Taban.AddDays(5), CikisTarihi = Taban.AddDays(5)
            });
            db.Baflar.Add(new Baf
            {
                No = "BAF-DK1", VehicleId = arac, Durum = BafStatus.Kapandi,
                CikisTarihi = Taban.AddDays(7), DonusTarihi = Taban.AddDays(8)
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await sp.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Taban, Taban.AddDays(9)));

        Assert.Equal("34DK01", row.Plaka);       // plaka DB'de normalize saklanır
        Assert.Equal(10, row.ToplamGun);         // ELLE: T+0 … T+9 dahil
        Assert.Equal(3, row.DoluGun);            // ELLE: {1,2,3}
        Assert.Equal(1, row.BakimGun);           // ELLE: {5}
        Assert.Equal(2, row.BafGun);             // ELLE: {7,8}
        Assert.Equal(4, row.BosGun);             // ELLE: {0,4,6,9}
        // Değişmez: kovalar çakışmaz, toplamları aralığa eşittir.
        Assert.Equal(row.ToplamGun, row.DoluGun + row.BakimGun + row.BafGun + row.BosGun);
    }

    [Fact]
    public async Task Cakisan_gunde_ONCELIK_uygulanir_toplam_araligi_ASMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var arac = await AracAsync(sp, "34 CK 01");
        var cari = await CariAsync(sp, "Çakışma A.Ş.");

        // ELLE: 3 günlük aralık (T+0..T+2). Kira T+0→T+2 (3 gün dolu) ile servis ve BAF AYNI
        // günleri kapsıyor. Öncelik Dolu > Bakım > Baf olduğu için üçü de dolu sayılır.
        await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(2), 100m);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SRV-CK1", VehicleId = arac, Durum = ServiceStatus.Tamamlandi,
                GirisTarihi = Taban, CikisTarihi = Taban.AddDays(2)
            });
            db.Baflar.Add(new Baf
            {
                No = "BAF-CK1", VehicleId = arac, Durum = BafStatus.Acik,
                CikisTarihi = Taban, DonusTarihi = null
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await sp.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Taban, Taban.AddDays(2)));

        Assert.Equal(3, row.ToplamGun);
        Assert.Equal(3, row.DoluGun);   // ELLE: üç gün de kirada — çakışan durumlar İKİNCİ kez sayılmaz
        Assert.Equal(0, row.BakimGun);
        Assert.Equal(0, row.BafGun);
        Assert.Equal(0, row.BosGun);    // negatife düşmez
    }

    [Fact]
    public async Task IPTAL_kira_servis_ve_BAF_arac_kirilimina_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var arac = await AracAsync(sp, "34 IP 01");
        var cari = await CariAsync(sp, "İptal A.Ş.");
        var kira = await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(2), 100m);
        await sp.GetRequiredService<RentalService>().CancelAsync(kira);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SRV-IP1", VehicleId = arac, Durum = ServiceStatus.Iptal,
                GirisTarihi = Taban, CikisTarihi = Taban.AddDays(2)
            });
            db.Baflar.Add(new Baf
            {
                No = "BAF-IP1", VehicleId = arac, Durum = BafStatus.Iptal,
                CikisTarihi = Taban, DonusTarihi = Taban.AddDays(2)
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await sp.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Taban, Taban.AddDays(2)));

        Assert.Equal(0, row.DoluGun);
        Assert.Equal(0, row.BakimGun);
        Assert.Equal(0, row.BafGun);
        Assert.Equal(3, row.BosGun);    // ELLE: üç günün üçü de boş — iptaller sayılmadı
    }

    [Fact]
    public async Task Arac_kirilimi_FILTRELERI_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();

        await AracAsync(sp, "34 FL 01", sube: "Şube A", grup: "Ekonomi", sipp: "ECMR", sahip: "Yüce Filo");
        await AracAsync(sp, "34 FL 02", sube: "Şube A", grup: "Lüks", sipp: "LDAR", sahip: "Beta Leasing");
        await AracAsync(sp, "06 FL 03", sube: "Şube B", grup: "Ekonomi", sipp: "ECMR", sahip: "Yüce Filo");

        async Task<int> Say(AracDurumTakipFilter f)
            => (await rapor.GetVehicleStatusTrackingByVehicleAsync(f, Taban, Taban)).Count;

        Assert.Equal(3, await Say(new AracDurumTakipFilter()));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { Sube = "Şube A" }));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { Grup = "Ekonomi" }));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { Sipp = "ECMR" }));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { AracSahibi = "Yüce Filo" }));
        Assert.Equal(1, await Say(new AracDurumTakipFilter { Plaka = "06 FL" }));   // boşluklu yazım normalize edilir
        // Kesişim: A şubesi + Ekonomi → yalnız 34 FL 01
        Assert.Equal("34FL01", Assert.Single(await rapor.GetVehicleStatusTrackingByVehicleAsync(
            new AracDurumTakipFilter { Sube = "Şube A", Grup = "Ekonomi" }, Taban, Taban)).Plaka);
        // Eşleşme yoksa boş liste (yanıltıcı "0 günlük araç" satırı üretilmez)
        Assert.Equal(0, await Say(new AracDurumTakipFilter { Sube = "Yok Şube" }));
    }

    [Fact]
    public async Task Gun_kirilimi_REGRESYONSUZ_ve_yeni_filtrelerle_uyumlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();

        var a1 = await AracAsync(sp, "34 GR 01", sube: "Şube A", grup: "Ekonomi");
        await AracAsync(sp, "34 GR 02", sube: "Şube B", grup: "Lüks");
        var cari = await CariAsync(sp, "Regresyon A.Ş.");
        await KiraAsync(sp, cari, a1, Taban, Taban.AddDays(1), 100m);

        // Eski imza (şube-only) BİREBİR eskisi gibi çalışmalı.
        var gun = Assert.Single(await rapor.GetVehicleStatusTrackingAsync(Taban, Taban));
        Assert.Equal(2, gun.ToplamArac);   // ELLE: iki araç
        Assert.Equal(1, gun.Dolu);         // ELLE: yalnız a1 kirada
        Assert.Equal(1, gun.Bos);

        var subeli = Assert.Single(await rapor.GetVehicleStatusTrackingAsync(Taban, Taban, "Şube A"));
        Assert.Equal(1, subeli.ToplamArac);
        Assert.Equal(1, subeli.Dolu);

        // Yeni filtreler gün görünümünde de geçerli (iki görünüm aynı araç kümesini anlatır).
        var grupla = Assert.Single(await rapor.GetVehicleStatusTrackingAsync(
            new AracDurumTakipFilter { Grup = "Lüks" }, Taban, Taban));
        Assert.Equal(1, grupla.ToplamArac);
        Assert.Equal(0, grupla.Dolu);
    }

    [Fact]
    public async Task Arac_kirilimi_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await AracAsync(s1.ServiceProvider, "34 GZ 01");

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Taban, Taban.AddDays(9)));
    }

    // ==================== Bölüm B — araç günlük durum ====================

    [Fact]
    public async Task Gunluk_pay_ELLE_HESAPLANAN_sabitlerle_birebir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var arac = await AracAsync(sp, "34 GD 01", grup: "Ekonomi", sipp: "ECMR", sahip: "Yüce Filo");
        var cari = await CariAsync(sp, "Günlük A.Ş.");
        // ELLE: 3 gün × 100 = 300 sözleşme tutarı → günlük pay 300 / 3 = 100.
        var kira = await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(3), 100m, ofis: "Merkez Ofis");

        var rapor = sp.GetRequiredService<ReportService>();
        var row = Assert.Single(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(1)));

        Assert.Equal("34GD01", row.Plaka);
        Assert.Equal("Günlük A.Ş.", row.Musteri);
        Assert.Equal("Merkez Ofis", row.CikisOfisi);
        Assert.Equal(kira, row.RentalId);
        Assert.Equal(3, row.Gun);
        Assert.Equal(100m, row.GunlukKira);      // ELLE: 300 / 3
        Assert.Equal(0m, row.GunlukHizmet);
        Assert.Equal(100m, row.GunlukToplam);

        // ELLE: 1 × 150 net, %20 KDV → 180 brüt ek hizmet → günlük hizmet 180 / 3 = 60.
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 150m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(kira, gps, 1m);

        var row2 = Assert.Single(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(1)));
        Assert.Equal(100m, row2.GunlukKira);     // baz kira payı DEĞİŞMEDİ
        Assert.Equal(60m, row2.GunlukHizmet);    // ELLE: 180 / 3
        Assert.Equal(160m, row2.GunlukToplam);   // ELLE: 100 + 60

        // TRY sözleşmede günlük toplam × gün == sözleşmenin genel toplamı (300 + 180 = 480).
        Assert.Equal(480m, row2.GunlukToplam * row2.Gun);
    }

    [Fact]
    public async Task Gunluk_durum_yalnizca_O_GUN_aktif_kiralari_gosterir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();

        var arac = await AracAsync(sp, "34 AK 01");
        var cari = await CariAsync(sp, "Aktif A.Ş.");
        // ELLE: kira T+2 → T+4. Aktif günler {2,3,4} (dönüş günü dahil — gün kırılımının
        // "Dolu" kovasıyla aynı kural). T+1 ve T+5 kapsam dışı.
        await KiraAsync(sp, cari, arac, Taban.AddDays(2), Taban.AddDays(4), 100m);

        Assert.Empty(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(1)));
        Assert.Single(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(2)));
        Assert.Single(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(4)));
        Assert.Empty(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(5)));

        // İptal kira hiçbir günde görünmez.
        var kira2 = await KiraAsync(sp, cari, await AracAsync(sp, "34 AK 02"),
            Taban.AddDays(2), Taban.AddDays(4), 100m);
        Assert.Equal(2, (await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(3))).Count);
        await sp.GetRequiredService<RentalService>().CancelAsync(kira2);
        Assert.Single(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(3)));
    }

    [Fact]
    public async Task Gunluk_durum_FILTRELERI_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();
        var cari = await CariAsync(sp, "Filtre A.Ş.");

        var a1 = await AracAsync(sp, "34 GF 01", grup: "Ekonomi", sipp: "ECMR", sahip: "Yüce Filo");
        var a2 = await AracAsync(sp, "06 GF 02", grup: "Lüks", sipp: "LDAR", sahip: "Beta Leasing");
        await KiraAsync(sp, cari, a1, Taban, Taban.AddDays(3), 100m, ofis: "Merkez Ofis");
        await KiraAsync(sp, cari, a2, Taban, Taban.AddDays(3), 200m, ofis: "Şube2 Ofis");

        var g = Taban.AddDays(1);
        async Task<int> Say(AracGunlukDurumFilter f) => (await rapor.GetVehicleDailyStatusAsync(g, f)).Count;

        Assert.Equal(2, (await rapor.GetVehicleDailyStatusAsync(g)).Count);
        Assert.Equal(1, await Say(new AracGunlukDurumFilter { Grup = "Ekonomi" }));
        Assert.Equal(1, await Say(new AracGunlukDurumFilter { Sipp = "LDAR" }));
        Assert.Equal(1, await Say(new AracGunlukDurumFilter { AracSahibi = "Beta Leasing" }));
        Assert.Equal(1, await Say(new AracGunlukDurumFilter { Ofis = "Merkez Ofis" }));
        Assert.Equal(1, await Say(new AracGunlukDurumFilter { Plaka = "06 GF" }));
        Assert.Equal(0, await Say(new AracGunlukDurumFilter { Grup = "Yok" }));
    }

    /// <summary>
    /// KIRILGAN REGRESYON KİLİDİ — "P&amp;L YALNIZ DEFTERDEN". Günlük durum raporu sözleşme
    /// tutarından PROJEKSİYON üretir; bu rakam hiçbir yere postlanmaz. Faturasız/tahsilatsız bir
    /// kira defterde gelir DOĞURMAZ → Kârlılık toplamı 0 kalmalıdır. Projeksiyon bir gün deftere
    /// sızarsa (veya raporlar kaynak-varlık tutarını toplarsa) bu test kırmızı yanar.
    /// </summary>
    [Fact]
    public async Task Gunluk_durum_PROJEKSIYONDUR_defter_PL_ini_DEGISTIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();

        var arac = await AracAsync(sp, "34 PL 01");
        var cari = await CariAsync(sp, "Projeksiyon A.Ş.");
        // ELLE: 10 gün × 1000 = 10.000 sözleşme tutarı — hiç faturalanmadı, hiç tahsil edilmedi.
        await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(10), 1000m);

        var row = Assert.Single(await rapor.GetVehicleDailyStatusAsync(Taban.AddDays(3)));
        Assert.Equal(1000m, row.GunlukToplam);   // ELLE: 10.000 / 10 — projeksiyon çalışıyor

        var karlilik = await rapor.GetProfitabilityAsync();
        Assert.Equal(0m, karlilik.ToplamGelir);  // DEFTER boş — projeksiyon P&L'e SIZMADI
        Assert.Equal(0m, karlilik.ToplamGider);
        Assert.Equal(0m, karlilik.ToplamNetKar);

        var gelirGider = await rapor.GetRevenueExpenseAsync();
        Assert.Equal(0m, gelirGider.GelirToplam);
    }

    [Fact]
    public async Task Gunluk_durum_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var arac = await AracAsync(sp, "34 GZ 11");
            var cari = await CariAsync(sp, "Gizli A.Ş.");
            await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(3), 100m);
            Assert.Single(await sp.GetRequiredService<ReportService>()
                .GetVehicleDailyStatusAsync(Taban.AddDays(1)));
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetVehicleDailyStatusAsync(Taban.AddDays(1)));
    }

    // ==================== Bölüm C — ek hizmet araç pivotu ====================

    [Fact]
    public async Task Arac_pivotu_ELLE_HESAPLANAN_hucrelerle_ve_ozetle_MUTABIK()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();
        var kalemler = sp.GetRequiredService<RentalAddOnService>();

        var cari = await CariAsync(sp, "Pivot A.Ş.");
        var a1 = await AracAsync(sp, "34 PV 01", grup: "Ekonomi", sipp: "ECMR");
        var a2 = await AracAsync(sp, "06 PV 02", grup: "Lüks", sipp: "LDAR");
        var k1 = await KiraAsync(sp, cari, a1, Taban, Taban.AddDays(3), 100m);
        var k2 = await KiraAsync(sp, cari, a2, Taban, Taban.AddDays(3), 100m);

        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);      // 1 adet → 100 net + 20 KDV = 120 brüt
        var bbk = await TanimAsync(sp, "BBK", "Bebek Koltuğu", 50m);    // 1 adet → 50 net + 10 KDV = 60 brüt

        // ELLE: a1 → 2×GPS (240) + 1×BBK (60) = 300 ; a2 → 1×GPS (120) = 120 ; genel 420.
        await kalemler.AddAsync(k1, gps, 2m);
        await kalemler.AddAsync(k1, bbk, 1m);
        await kalemler.AddAsync(k2, gps, 1m);

        var pivot = await rapor.GetAddOnVehiclePivotAsync();

        // Sütun sırası: en çok satan hizmet solda → GPS (360) sonra Bebek Koltuğu (60).
        Assert.Equal(new[] { "Navigasyon", "Bebek Koltuğu" }, pivot.Kolonlar);
        Assert.Equal(2, pivot.Satirlar.Count);

        var s1 = pivot.Satirlar.Single(x => x.Plaka == "34PV01");
        Assert.Equal("Ekonomi", s1.Grup);
        Assert.Equal("ECMR", s1.Sipp);
        Assert.Equal(240m, s1.Hucreler[0]);   // ELLE: 2 × 120
        Assert.Equal(60m, s1.Hucreler[1]);    // ELLE: 1 × 60
        Assert.Equal(300m, s1.Toplam);
        Assert.Equal(2, s1.KalemAdet);

        var s2 = pivot.Satirlar.Single(x => x.Plaka == "06PV02");
        Assert.Equal(120m, s2.Hucreler[0]);
        Assert.Equal(0m, s2.Hucreler[1]);     // bu araca bebek koltuğu satılmadı
        Assert.Equal(120m, s2.Toplam);

        Assert.Equal(new[] { 360m, 60m }, pivot.KolonToplam);
        Assert.Equal(420m, pivot.GenelToplam);

        // PARİTE KİLİDİ: pivot yeni para üretmez — ad-bazlı özetin brütüyle BİREBİR aynı.
        var ozet = await rapor.GetAddOnReportAsync();
        Assert.Equal(420m, ozet.ToplamBrut);
        Assert.Equal(ozet.ToplamBrut, pivot.GenelToplam);
        Assert.Equal(ozet.Satirlar.Count, pivot.Kolonlar.Count);
    }

    [Fact]
    public async Task Arac_pivotu_TARIH_penceresi_ve_IPTAL_kurallarinda_ozetle_AYNI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var rapor = sp.GetRequiredService<ReportService>();

        var cari = await CariAsync(sp, "Pencere A.Ş.");
        var arac = await AracAsync(sp, "34 PN 01");
        var kira = await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(3), 100m);
        var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(kira, gps, 1m);

        // Kalem BUGÜN eklendi → dünde biten pencere onu görmez (özet de görmez).
        var dun = TestZaman.Simdi().AddDays(-1);
        Assert.Equal(0m, (await rapor.GetAddOnVehiclePivotAsync(to: dun)).GenelToplam);
        Assert.Equal(0m, (await rapor.GetAddOnReportAsync(to: dun)).ToplamBrut);

        Assert.Equal(120m, (await rapor.GetAddOnVehiclePivotAsync()).GenelToplam);

        // İptal kira: her iki görünümde de kaybolur (ayrışırlarsa raporlar birbirini tutmaz).
        await sp.GetRequiredService<RentalService>().CancelAsync(kira);
        Assert.Equal(0m, (await rapor.GetAddOnVehiclePivotAsync()).GenelToplam);
        Assert.Equal(0m, (await rapor.GetAddOnReportAsync()).ToplamBrut);
        Assert.Empty((await rapor.GetAddOnVehiclePivotAsync()).Satirlar);
    }

    [Fact]
    public async Task Arac_pivotu_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var cari = await CariAsync(sp, "Gizli Pivot A.Ş.");
            var arac = await AracAsync(sp, "34 GZ 21");
            var kira = await KiraAsync(sp, cari, arac, Taban, Taban.AddDays(3), 100m);
            var gps = await TanimAsync(sp, "GPS", "Navigasyon", 100m);
            await sp.GetRequiredService<RentalAddOnService>().AddAsync(kira, gps, 1m);
            Assert.Equal(120m, (await sp.GetRequiredService<ReportService>()
                .GetAddOnVehiclePivotAsync()).GenelToplam);
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var pivot = await s2.ServiceProvider.GetRequiredService<ReportService>().GetAddOnVehiclePivotAsync();
        Assert.Empty(pivot.Satirlar);
        Assert.Equal(0m, pivot.GenelToplam);
    }
}
