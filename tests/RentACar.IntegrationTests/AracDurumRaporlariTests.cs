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
    private static DateTimeOffset Base => new(TestZaman.Now().UtcDateTime.Date, TimeSpan.Zero);

    private static Task<Guid> VehicleAsync(IServiceProvider sp, string plate,
        string? branch = null, string? group = null, string? sipp = null, string? owner = null)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = plate, Sube = branch, Grup = group, Sipp = sipp, AracSahibi = owner });

    private static Task<Guid> CustomerAsync(IServiceProvider sp, string title)
        => sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = title });

    private static Task<Guid> RentalAsync(IServiceProvider sp, Guid account, Guid vehicle,
        DateTimeOffset start, DateTimeOffset bit, decimal daily, string? office = null)
        => sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, BasTar = start, BitTar = bit,
            GunlukUcret = daily, CikisOfisi = office
        });

    private static Task<Guid> DefinitionAsync(IServiceProvider sp, string code, string name, decimal fee)
        => sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(new EkHizmetTanimInput
        { Kod = code, Ad = name, BirimUcret = fee, KdvOrani = 0.20m });

    // ==================== Bölüm A — araç kırılımı ====================

    [Fact]
    public async Task Arac_bazli_kovalar_ELLE_SAYILAN_GUNLERLE_birebir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var vehicle = await VehicleAsync(sp, "34 DK 01");
        var account = await CustomerAsync(sp, "Durum A.Ş.");

        // ELLE KURULAN TAKVİM — aralık T+0 … T+9 (10 gün):
        //   kira   T+1 → T+3  → dolu günler {1,2,3}          = 3
        //   servis T+5 → T+5  → bakım günü  {5}              = 1
        //   BAF    T+7 → T+8  → baf günleri {7,8}            = 2
        //   kalan  {0,4,6,9}  → boş                          = 4
        await RentalAsync(sp, account, vehicle, Base.AddDays(1), Base.AddDays(3), 100m);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SRV-DK1", VehicleId = vehicle, Durum = ServiceStatus.Tamamlandi,
                GirisTarihi = Base.AddDays(5), CikisTarihi = Base.AddDays(5)
            });
            db.Baflar.Add(new Baf
            {
                No = "BAF-DK1", VehicleId = vehicle, Durum = BafStatus.Kapandi,
                CikisTarihi = Base.AddDays(7), DonusTarihi = Base.AddDays(8)
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await sp.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Base, Base.AddDays(9)));

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

        var vehicle = await VehicleAsync(sp, "34 CK 01");
        var account = await CustomerAsync(sp, "Çakışma A.Ş.");

        // ELLE: 3 günlük aralık (T+0..T+2). Kira T+0→T+2 (3 gün dolu) ile servis ve BAF AYNI
        // günleri kapsıyor. Öncelik Dolu > Bakım > Baf olduğu için üçü de dolu sayılır.
        await RentalAsync(sp, account, vehicle, Base, Base.AddDays(2), 100m);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SRV-CK1", VehicleId = vehicle, Durum = ServiceStatus.Tamamlandi,
                GirisTarihi = Base, CikisTarihi = Base.AddDays(2)
            });
            db.Baflar.Add(new Baf
            {
                No = "BAF-CK1", VehicleId = vehicle, Durum = BafStatus.Acik,
                CikisTarihi = Base, DonusTarihi = null
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await sp.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Base, Base.AddDays(2)));

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

        var vehicle = await VehicleAsync(sp, "34 IP 01");
        var account = await CustomerAsync(sp, "İptal A.Ş.");
        var rental = await RentalAsync(sp, account, vehicle, Base, Base.AddDays(2), 100m);
        await sp.GetRequiredService<RentalService>().CancelAsync(rental);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SRV-IP1", VehicleId = vehicle, Durum = ServiceStatus.Iptal,
                GirisTarihi = Base, CikisTarihi = Base.AddDays(2)
            });
            db.Baflar.Add(new Baf
            {
                No = "BAF-IP1", VehicleId = vehicle, Durum = BafStatus.Iptal,
                CikisTarihi = Base, DonusTarihi = Base.AddDays(2)
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await sp.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Base, Base.AddDays(2)));

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
        var report = sp.GetRequiredService<ReportService>();

        await VehicleAsync(sp, "34 FL 01", branch: "Şube A", group: "Ekonomi", sipp: "ECMR", owner: "Yüce Filo");
        await VehicleAsync(sp, "34 FL 02", branch: "Şube A", group: "Lüks", sipp: "LDAR", owner: "Beta Leasing");
        await VehicleAsync(sp, "06 FL 03", branch: "Şube B", group: "Ekonomi", sipp: "ECMR", owner: "Yüce Filo");

        async Task<int> Say(AracDurumTakipFilter f)
            => (await report.GetVehicleStatusTrackingByVehicleAsync(f, Base, Base)).Count;

        Assert.Equal(3, await Say(new AracDurumTakipFilter()));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { Sube = "Şube A" }));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { Grup = "Ekonomi" }));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { Sipp = "ECMR" }));
        Assert.Equal(2, await Say(new AracDurumTakipFilter { AracSahibi = "Yüce Filo" }));
        Assert.Equal(1, await Say(new AracDurumTakipFilter { Plaka = "06 FL" }));   // boşluklu yazım normalize edilir
        // Kesişim: A şubesi + Ekonomi → yalnız 34 FL 01
        Assert.Equal("34FL01", Assert.Single(await report.GetVehicleStatusTrackingByVehicleAsync(
            new AracDurumTakipFilter { Sube = "Şube A", Grup = "Ekonomi" }, Base, Base)).Plaka);
        // Eşleşme yoksa boş liste (yanıltıcı "0 günlük araç" satırı üretilmez)
        Assert.Equal(0, await Say(new AracDurumTakipFilter { Sube = "Yok Şube" }));
    }

    [Fact]
    public async Task Gun_kirilimi_REGRESYONSUZ_ve_yeni_filtrelerle_uyumlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();

        var a1 = await VehicleAsync(sp, "34 GR 01", branch: "Şube A", group: "Ekonomi");
        await VehicleAsync(sp, "34 GR 02", branch: "Şube B", group: "Lüks");
        var account = await CustomerAsync(sp, "Regresyon A.Ş.");
        await RentalAsync(sp, account, a1, Base, Base.AddDays(1), 100m);

        // Eski imza (şube-only) BİREBİR eskisi gibi çalışmalı.
        var day = Assert.Single(await report.GetVehicleStatusTrackingAsync(Base, Base));
        Assert.Equal(2, day.ToplamArac);   // ELLE: iki araç
        Assert.Equal(1, day.Dolu);         // ELLE: yalnız a1 kirada
        Assert.Equal(1, day.Bos);

        var withBranch = Assert.Single(await report.GetVehicleStatusTrackingAsync(Base, Base, "Şube A"));
        Assert.Equal(1, withBranch.ToplamArac);
        Assert.Equal(1, withBranch.Dolu);

        // Yeni filtreler gün görünümünde de geçerli (iki görünüm aynı araç kümesini anlatır).
        var groupBy = Assert.Single(await report.GetVehicleStatusTrackingAsync(
            new AracDurumTakipFilter { Grup = "Lüks" }, Base, Base));
        Assert.Equal(1, groupBy.ToplamArac);
        Assert.Equal(0, groupBy.Dolu);
    }

    [Fact]
    public async Task Arac_kirilimi_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await VehicleAsync(s1.ServiceProvider, "34 GZ 01");

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetVehicleStatusTrackingByVehicleAsync(null, Base, Base.AddDays(9)));
    }

    // ==================== Bölüm B — araç günlük durum ====================

    [Fact]
    public async Task Gunluk_pay_ELLE_HESAPLANAN_sabitlerle_birebir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var vehicle = await VehicleAsync(sp, "34 GD 01", group: "Ekonomi", sipp: "ECMR", owner: "Yüce Filo");
        var account = await CustomerAsync(sp, "Günlük A.Ş.");
        // ELLE: 3 gün × 100 = 300 sözleşme tutarı → günlük pay 300 / 3 = 100.
        var rental = await RentalAsync(sp, account, vehicle, Base, Base.AddDays(3), 100m, office: "Merkez Ofis");

        var report = sp.GetRequiredService<ReportService>();
        var row = Assert.Single(await report.GetVehicleDailyStatusAsync(Base.AddDays(1)));

        Assert.Equal("34GD01", row.Plaka);
        Assert.Equal("Günlük A.Ş.", row.Musteri);
        Assert.Equal("Merkez Ofis", row.CikisOfisi);
        Assert.Equal(rental, row.RentalId);
        Assert.Equal(3, row.Gun);
        Assert.Equal(100m, row.GunlukKira);      // ELLE: 300 / 3
        Assert.Equal(0m, row.GunlukHizmet);
        Assert.Equal(100m, row.GunlukToplam);

        // ELLE: 1 × 150 net, %20 KDV → 180 brüt ek hizmet → günlük hizmet 180 / 3 = 60.
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 150m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, gps, 1m);

        var row2 = Assert.Single(await report.GetVehicleDailyStatusAsync(Base.AddDays(1)));
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
        var report = sp.GetRequiredService<ReportService>();

        var vehicle = await VehicleAsync(sp, "34 AK 01");
        var account = await CustomerAsync(sp, "Aktif A.Ş.");
        // ELLE: kira T+2 → T+4. Aktif günler {2,3,4} (dönüş günü dahil — gün kırılımının
        // "Dolu" kovasıyla aynı kural). T+1 ve T+5 kapsam dışı.
        await RentalAsync(sp, account, vehicle, Base.AddDays(2), Base.AddDays(4), 100m);

        Assert.Empty(await report.GetVehicleDailyStatusAsync(Base.AddDays(1)));
        Assert.Single(await report.GetVehicleDailyStatusAsync(Base.AddDays(2)));
        Assert.Single(await report.GetVehicleDailyStatusAsync(Base.AddDays(4)));
        Assert.Empty(await report.GetVehicleDailyStatusAsync(Base.AddDays(5)));

        // İptal kira hiçbir günde görünmez.
        var rental2 = await RentalAsync(sp, account, await VehicleAsync(sp, "34 AK 02"),
            Base.AddDays(2), Base.AddDays(4), 100m);
        Assert.Equal(2, (await report.GetVehicleDailyStatusAsync(Base.AddDays(3))).Count);
        await sp.GetRequiredService<RentalService>().CancelAsync(rental2);
        Assert.Single(await report.GetVehicleDailyStatusAsync(Base.AddDays(3)));
    }

    [Fact]
    public async Task Gunluk_durum_FILTRELERI_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();
        var account = await CustomerAsync(sp, "Filtre A.Ş.");

        var a1 = await VehicleAsync(sp, "34 GF 01", group: "Ekonomi", sipp: "ECMR", owner: "Yüce Filo");
        var a2 = await VehicleAsync(sp, "06 GF 02", group: "Lüks", sipp: "LDAR", owner: "Beta Leasing");
        await RentalAsync(sp, account, a1, Base, Base.AddDays(3), 100m, office: "Merkez Ofis");
        await RentalAsync(sp, account, a2, Base, Base.AddDays(3), 200m, office: "Şube2 Ofis");

        var g = Base.AddDays(1);
        async Task<int> Say(AracGunlukDurumFilter f) => (await report.GetVehicleDailyStatusAsync(g, f)).Count;

        Assert.Equal(2, (await report.GetVehicleDailyStatusAsync(g)).Count);
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
        var report = sp.GetRequiredService<ReportService>();

        var vehicle = await VehicleAsync(sp, "34 PL 01");
        var account = await CustomerAsync(sp, "Projeksiyon A.Ş.");
        // ELLE: 10 gün × 1000 = 10.000 sözleşme tutarı — hiç faturalanmadı, hiç tahsil edilmedi.
        await RentalAsync(sp, account, vehicle, Base, Base.AddDays(10), 1000m);

        var row = Assert.Single(await report.GetVehicleDailyStatusAsync(Base.AddDays(3)));
        Assert.Equal(1000m, row.GunlukToplam);   // ELLE: 10.000 / 10 — projeksiyon çalışıyor

        var profitability = await report.GetProfitabilityAsync();
        Assert.Equal(0m, profitability.ToplamGelir);  // DEFTER boş — projeksiyon P&L'e SIZMADI
        Assert.Equal(0m, profitability.ToplamGider);
        Assert.Equal(0m, profitability.ToplamNetKar);

        var revenueExpense = await report.GetRevenueExpenseAsync();
        Assert.Equal(0m, revenueExpense.GelirToplam);
    }

    [Fact]
    public async Task Gunluk_durum_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var vehicle = await VehicleAsync(sp, "34 GZ 11");
            var account = await CustomerAsync(sp, "Gizli A.Ş.");
            await RentalAsync(sp, account, vehicle, Base, Base.AddDays(3), 100m);
            Assert.Single(await sp.GetRequiredService<ReportService>()
                .GetVehicleDailyStatusAsync(Base.AddDays(1)));
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetVehicleDailyStatusAsync(Base.AddDays(1)));
    }

    // ==================== Bölüm C — ek hizmet araç pivotu ====================

    [Fact]
    public async Task Arac_pivotu_ELLE_HESAPLANAN_hucrelerle_ve_ozetle_MUTABIK()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();
        var items = sp.GetRequiredService<RentalAddOnService>();

        var account = await CustomerAsync(sp, "Pivot A.Ş.");
        var a1 = await VehicleAsync(sp, "34 PV 01", group: "Ekonomi", sipp: "ECMR");
        var a2 = await VehicleAsync(sp, "06 PV 02", group: "Lüks", sipp: "LDAR");
        var k1 = await RentalAsync(sp, account, a1, Base, Base.AddDays(3), 100m);
        var k2 = await RentalAsync(sp, account, a2, Base, Base.AddDays(3), 100m);

        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);      // 1 adet → 100 net + 20 KDV = 120 brüt
        var bbk = await DefinitionAsync(sp, "BBK", "Bebek Koltuğu", 50m);    // 1 adet → 50 net + 10 KDV = 60 brüt

        // ELLE: a1 → 2×GPS (240) + 1×BBK (60) = 300 ; a2 → 1×GPS (120) = 120 ; genel 420.
        await items.AddAsync(k1, gps, 2m);
        await items.AddAsync(k1, bbk, 1m);
        await items.AddAsync(k2, gps, 1m);

        var pivot = await report.GetAddOnVehiclePivotAsync();

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
        var summary = await report.GetAddOnReportAsync();
        Assert.Equal(420m, summary.ToplamBrut);
        Assert.Equal(summary.ToplamBrut, pivot.GenelToplam);
        Assert.Equal(summary.Satirlar.Count, pivot.Kolonlar.Count);
    }

    [Fact]
    public async Task Arac_pivotu_TARIH_penceresi_ve_IPTAL_kurallarinda_ozetle_AYNI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var report = sp.GetRequiredService<ReportService>();

        var account = await CustomerAsync(sp, "Pencere A.Ş.");
        var vehicle = await VehicleAsync(sp, "34 PN 01");
        var rental = await RentalAsync(sp, account, vehicle, Base, Base.AddDays(3), 100m);
        var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, gps, 1m);

        // Kalem BUGÜN eklendi → dünde biten pencere onu görmez (özet de görmez).
        var dun = TestZaman.Now().AddDays(-1);
        Assert.Equal(0m, (await report.GetAddOnVehiclePivotAsync(to: dun)).GenelToplam);
        Assert.Equal(0m, (await report.GetAddOnReportAsync(to: dun)).ToplamBrut);

        Assert.Equal(120m, (await report.GetAddOnVehiclePivotAsync()).GenelToplam);

        // İptal kira: her iki görünümde de kaybolur (ayrışırlarsa raporlar birbirini tutmaz).
        await sp.GetRequiredService<RentalService>().CancelAsync(rental);
        Assert.Equal(0m, (await report.GetAddOnVehiclePivotAsync()).GenelToplam);
        Assert.Equal(0m, (await report.GetAddOnReportAsync()).ToplamBrut);
        Assert.Empty((await report.GetAddOnVehiclePivotAsync()).Satirlar);
    }

    [Fact]
    public async Task Arac_pivotu_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var account = await CustomerAsync(sp, "Gizli Pivot A.Ş.");
            var vehicle = await VehicleAsync(sp, "34 GZ 21");
            var rental = await RentalAsync(sp, account, vehicle, Base, Base.AddDays(3), 100m);
            var gps = await DefinitionAsync(sp, "GPS", "Navigasyon", 100m);
            await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, gps, 1m);
            Assert.Equal(120m, (await sp.GetRequiredService<ReportService>()
                .GetAddOnVehiclePivotAsync()).GenelToplam);
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var pivot = await s2.ServiceProvider.GetRequiredService<ReportService>().GetAddOnVehiclePivotAsync();
        Assert.Empty(pivot.Satirlar);
        Assert.Equal(0m, pivot.GenelToplam);
    }
}
