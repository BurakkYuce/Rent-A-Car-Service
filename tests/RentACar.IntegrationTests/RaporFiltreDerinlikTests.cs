using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-76 — rapor filtre derinliği + İKİ VERİ-DOĞRULUĞU DÜZELTMESİ.
///
/// <para><b>Düzeltme 1:</b> araç durum-takip raporunda İPTAL servis kayıtları "Bakım" günü olarak
/// sayılıyordu (filtre eksikti) → iptal edilen bir servis aracı günlerce bakımdaymış gibi
/// gösteriyor ve "Boş" sayısını düşürüyordu.</para>
///
/// <para><b>Düzeltme 2:</b> rezervasyon kaynak raporunda İPTAL rezervasyonlar adet/gün/CİROYA
/// dahil ediliyordu → gerçekleşmemiş iş ciro sayılıyordu.</para>
///
/// <para><b>Regresyon koruması:</b> periyodik servis sorgusu FiloBildirimUretici ile PAYLAŞILIYOR;
/// filtre opsiyonel eklendi ve parametresiz çağrı davranışı BİREBİR aynı kaldı.</para>
/// </summary>
[Collection("postgres")]
public sealed class RaporFiltreDerinlikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private static Task<Guid> VehicleAsync(IServiceProvider sp, string plate, string? branch = null,
        string? brand = null, int km = 10_000, VehicleStatus status = VehicleStatus.Musait)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = plate, Sube = branch, Marka = brand, Km = km, Durum = status });

    // ---------------- Düzeltme 1: iptal servis "bakım" sayılmıyor ----------------

    [Fact]
    public async Task IPTAL_servis_BAKIM_gunu_olarak_SAYILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var v1 = await VehicleAsync(sp, "34 AD 01");
        var v2 = await VehicleAsync(sp, "34 AD 02");

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            // ELLE: v1 GERÇEKTEN serviste (10-12 Haziran), v2'nin servisi İPTAL.
            // ServiceRecord.No tenant içinde BENZERSİZ — ham insert'te elle verilmeli.
            db.ServiceRecords.Add(new ServiceRecord
            { No = "SRV-A1", VehicleId = v1, GirisTarihi = T0, CikisTarihi = T0.AddDays(2), Durum = ServiceStatus.Tamamlandi });
            db.ServiceRecords.Add(new ServiceRecord
            { No = "SRV-A2", VehicleId = v2, GirisTarihi = T0, CikisTarihi = T0.AddDays(2), Durum = ServiceStatus.Iptal });
            await db.SaveChangesAsync();
        }

        var rows = await sp.GetRequiredService<ReportService>().GetVehicleStatusTrackingAsync(T0, T0.AddDays(1));
        var day = rows.Single(r => r.Gun.Date == T0.Date);

        Assert.Equal(2, day.ToplamArac);
        Assert.Equal(1, day.Bakim);          // ELLE: yalnız v1 — iptal olan SAYILMADI
        Assert.Equal(1, day.Bos);            // ELLE: 2 − 0 kirada − 1 bakım
    }

    [Fact]
    public async Task Arac_durum_takip_SUBE_filtresi_ve_BAF_kolonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var a1 = await VehicleAsync(sp, "34 SB 01", "Sube A");
        await VehicleAsync(sp, "34 SB 02", "Sube A");
        await VehicleAsync(sp, "34 SB 03", "Sube B");

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Baflar.Add(new Baf { No = "BAF-000001", VehicleId = a1, Durum = BafStatus.Acik, CreatedAtUtc = T0 });
            db.Baflar.Add(new Baf { No = "BAF-000002", VehicleId = a1, Durum = BafStatus.Kapandi, CreatedAtUtc = T0 });
            await db.SaveChangesAsync();
        }

        var svc = sp.GetRequiredService<ReportService>();
        Assert.Equal(3, (await svc.GetVehicleStatusTrackingAsync(T0, T0)).Single().ToplamArac);

        var a = (await svc.GetVehicleStatusTrackingAsync(T0, T0, "Sube A")).Single();
        Assert.Equal(2, a.ToplamArac);       // ELLE: yalnız A şubesi
        Assert.Equal(1, a.ToplamBaf);        // ELLE: 2 BAF'tan yalnız 1'i Açık

        var b = (await svc.GetVehicleStatusTrackingAsync(T0, T0, "Sube B")).Single();
        Assert.Equal(1, b.ToplamArac);
        Assert.Equal(0, b.ToplamBaf);

        // Aracı olmayan şube → boş liste (yanıltıcı "0 araçlı gün" satırı üretilmez).
        Assert.Empty(await svc.GetVehicleStatusTrackingAsync(T0, T0, "Yok Şube"));
    }

    // ---------------- Düzeltme 2: iptal rezervasyon ciroya girmiyor ----------------

    [Fact]
    public async Task IPTAL_rezervasyon_ADET_GUN_CIROYA_girmez_ama_gorunur_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var vehicle = await VehicleAsync(sp, "34 RK 01", "Merkez");
        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Rez A.Ş." });

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            // ELLE: Web kaynağından 2 geçerli (3 gün/1000 + 2 gün/500) + 1 İPTAL (5 gün/9000).
            db.Reservations.AddRange(
                new Reservation { ReservationNo = "RZ-000001", MusteriId = account, VehicleId = vehicle,
                    BasTar = T0, BitTar = T0.AddDays(3), Gun = 3, Tutar = 1000m, Kaynak = "Web", CikisOfisi = "Merkez" },
                new Reservation { ReservationNo = "RZ-000002", MusteriId = account, VehicleId = vehicle,
                    BasTar = T0, BitTar = T0.AddDays(2), Gun = 2, Tutar = 500m, Kaynak = "Web", CikisOfisi = "Merkez" },
                new Reservation { ReservationNo = "RZ-000003", MusteriId = account, VehicleId = vehicle,
                    BasTar = T0, BitTar = T0.AddDays(5), Gun = 5, Tutar = 9000m, Kaynak = "Web",
                    CikisOfisi = "Merkez", Durum = ReservationStatus.Iptal });
            await db.SaveChangesAsync();
        }

        var svc = sp.GetRequiredService<ReportService>();
        var web = Assert.Single(await svc.GetReservationSourceAsync());
        Assert.Equal("Web", web.Kaynak);
        Assert.Equal(2, web.Adet);              // ELLE: iptal HARİÇ
        Assert.Equal(5, web.ToplamGun);         // ELLE: 3 + 2
        Assert.Equal(1500m, web.ToplamCiro);    // ELLE: 1000 + 500 — 9000 GİRMEDİ
        Assert.Equal(1, web.IptalAdet);         // görünür kalıyor

        // İstenirse dahil edilebilir (eski davranış).
        var included = Assert.Single(await svc.GetReservationSourceAsync(
            new RezervasyonKaynakFilter { IptalleriDahilEt = true }));
        Assert.Equal(3, included.Adet);
        Assert.Equal(10500m, included.ToplamCiro);
    }

    [Fact]
    public async Task Rezervasyon_kaynak_OFIS_GRUP_ve_TARIH_ALANI_filtreleri()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var economyVehicle = await VehicleAsync(sp, "34 RK 10", brand: "Fiat");
        var luxVehicle = await VehicleAsync(sp, "34 RK 11", brand: "BMW");
        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Rez A.Ş." });

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            var e = await db.Vehicles.FirstAsync(v => v.Id == economyVehicle);
            e.Grup = "EKO";
            var l = await db.Vehicles.FirstAsync(v => v.Id == luxVehicle);
            l.Grup = "LUX";

            // ELLE: A ofisi/EKO — çıkış 10 Haz, dönüş 13 Haz. B ofisi/LUX — çıkış 20 Haz.
            db.Reservations.AddRange(
                new Reservation { ReservationNo = "RZ-000010", MusteriId = account, VehicleId = economyVehicle,
                    BasTar = T0, BitTar = T0.AddDays(3), Gun = 3, Tutar = 1000m, Kaynak = "Web", CikisOfisi = "Ofis A" },
                new Reservation { ReservationNo = "RZ-000011", MusteriId = account, VehicleId = luxVehicle,
                    BasTar = T0.AddDays(10), BitTar = T0.AddDays(12), Gun = 2, Tutar = 4000m, Kaynak = "Acenta", CikisOfisi = "Ofis B" });
            await db.SaveChangesAsync();
        }

        var svc = sp.GetRequiredService<ReportService>();
        Assert.Equal(2, (await svc.GetReservationSourceAsync()).Count);
        Assert.Equal("Web", Assert.Single(await svc.GetReservationSourceAsync(
            new RezervasyonKaynakFilter { Ofis = "Ofis A" })).Kaynak);
        Assert.Equal("Acenta", Assert.Single(await svc.GetReservationSourceAsync(
            new RezervasyonKaynakFilter { Grup = "LUX" })).Kaynak);

        // TARİH ALANI: 12 Haziran'a kadar ÇIKIŞ → yalnız 1.; aynı tarihe kadar DÖNÜŞ → yine 1.
        // ama 13 Haziran'a kadar dönüş → 1. dahil (bitişi 13'ünde).
        var pickup = await svc.GetReservationSourceAsync(
            new RezervasyonKaynakFilter { Bit = T0.AddDays(5), TarihTipi = "Cikis" });
        Assert.Equal("Web", Assert.Single(pickup).Kaynak);

        var returnInfo = await svc.GetReservationSourceAsync(
            new RezervasyonKaynakFilter { Bas = T0.AddDays(11), TarihTipi = "Donus" });
        Assert.Equal("Acenta", Assert.Single(returnInfo).Kaynak);   // ELLE: yalnız 12 Haz'da dönen
    }

    // ---------------- Periyodik servis: paylaşılan sorgu regresyonu ----------------

    [Fact]
    public async Task Periyodik_servis_PARAMETRESIZ_cagri_DEGISMEDI_filtre_yalniz_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await VehicleAsync(sp, "34 PS 01", "Sube A", "Fiat", km: 9_000);
        await VehicleAsync(sp, "34 PS 02", "Sube B", "Opel", km: 5_000);
        await VehicleAsync(sp, "34 PS 03", "Sube A", "BMW", km: 1_000, status: VehicleStatus.Pasif);

        var svc = sp.GetRequiredService<ReportService>();

        // Parametresiz: TÜM araçlar (hedefi olmayan da) — FiloBildirimUretici bu yolu kullanıyor.
        var all = await svc.GetPeriodicServiceAsync();
        Assert.Equal(3, all.Count);
        Assert.All(all, r => Assert.Null(r.SonrakiBakimKm));   // tanım yok → gizlenmiyor

        // Filtreler YALNIZ daraltır.
        Assert.Equal(2, (await svc.GetPeriodicServiceAsync(new PeriyodikServisFilter { Sube = "sube a" })).Count);
        Assert.Equal(2, (await svc.GetPeriodicServiceAsync(new PeriyodikServisFilter { Aktif = true })).Count);
        Assert.Single(await svc.GetPeriodicServiceAsync(new PeriyodikServisFilter { Aktif = false }));
        // Plaka boşluklu girilebilir.
        Assert.Single(await svc.GetPeriodicServiceAsync(new PeriyodikServisFilter { Plaka = "34 PS 02" }));

        // Rapor kolonları dolu.
        var fiat = all.Single(r => r.Plaka == "34PS01");
        Assert.Equal("Fiat", fiat.Marka);
        Assert.Equal("Sube A", fiat.Sube);
        Assert.True(fiat.Aktif);
    }

    [Fact]
    public async Task Uyari_esigi_HEDEFSIZ_araci_ELEMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var v = await VehicleAsync(sp, "34 UE 01", km: 10_000);
        await VehicleAsync(sp, "34 UE 02", km: 10_000);   // hedefi YOK

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            // ELLE: hedef 30.000 → kalan 20.000 (eşiğin çok üstünde).
            db.ServiceRecords.Add(new ServiceRecord
            { No = "SRV-B1", VehicleId = v, GirisTarihi = T0, Durum = ServiceStatus.Tamamlandi,
              SonrakiBakimKm = 30_000, GirisKm = 9_000 });
            await db.SaveChangesAsync();
        }

        var rows = await sp.GetRequiredService<ReportService>()
            .GetPeriodicServiceAsync(new PeriyodikServisFilter { UyariEsigi = 1000 });

        // ELLE: hedefi olan araç elendi (20.000 > 1.000); hedefi OLMAYAN araç KALDI — eksik tanım
        // da bir uyarıdır ve sessizce gizlenmemeli.
        Assert.Equal("34UE02", Assert.Single(rows).Plaka);
    }

    // ---------------- Günlük faaliyet: şube süzgeci ----------------

    [Fact]
    public async Task Gunluk_faaliyet_SUBE_filtresi_operasyon_sayaclarina_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var vehicle = await VehicleAsync(sp, "34 GF 01");
        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Gün A.Ş." });
        var today = TestZaman.Now();

        var rentals = sp.GetRequiredService<RentalService>();
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicle, BasTar = today, BitTar = today.AddDays(2),
          GunlukUcret = 1000m, CikisOfisi = "Ofis A" });
        var vehicle2 = await VehicleAsync(sp, "34 GF 02");
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = vehicle2, BasTar = today, BitTar = today.AddDays(2),
          GunlukUcret = 1000m, CikisOfisi = "Ofis B" });

        var svc = sp.GetRequiredService<ReportService>();
        Assert.Equal(2, (await svc.GetDailyActivityAsync(today)).Cikis);
        Assert.Equal(1, (await svc.GetDailyActivityAsync(today, "Ofis A")).Cikis);   // ELLE
        Assert.Equal(1, (await svc.GetDailyActivityAsync(today, "Ofis A")).YeniKira);
        Assert.Equal(0, (await svc.GetDailyActivityAsync(today, "Ofis C")).Cikis);
    }

    [Fact]
    public async Task Km_detay_arac_ve_sozlesme_kunyesi_tasiyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var vehicle = await VehicleAsync(sp, "34 KM 01", brand: "Fiat");
        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Km A.Ş." });

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "KS-KM1", MusteriId = account, VehicleId = vehicle, Durum = RentalStatus.Tamamlandi,
                BasTar = T0, BitTar = T0.AddDays(3), CikisKm = 1000, DonusKm = 1500,
                KmLimit = 300, FazlaKm = 200, FazlaKmBedeli = 400m, CreatedAtUtc = T0
            });
            await db.SaveChangesAsync();
        }

        var r = Assert.Single(await sp.GetRequiredService<ReportService>().GetKmDetailAsync(T0, T0.AddDays(1)));
        Assert.Equal("34KM01", r.Plaka);
        Assert.Equal("Fiat", r.Marka);          // FAZ-76: araç JOIN'i genişledi
        Assert.Equal(T0, r.BasTar);             // FAZ-76: sözleşme tarihleri
        Assert.Equal(500, r.KatedilenKm);       // ELLE: 1500 − 1000 (mevcut hesap değişmedi)
    }
}
