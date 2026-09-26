using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap N3 — CRM segment + personel çalışma (salt-okur agrega). BAĞIMSIZ ORACLE: 1 müşteri 2 kira (3 gün×100)
/// → kira 2 / ciro 600 / Standart; 1 personel 2 BAF → tahsis 2.
/// </summary>
[Collection("postgres")]
public sealed class CrmAnalizTests(PostgresFixture fx)
{
    [Fact]
    public async Task Musteri_segment_agrega()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var custId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Segment Müşteri" });
        var vehicles = sp.GetRequiredService<VehicleService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var start = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

        foreach (var plate in new[] { "34 CR 01", "34 CR 02" })
        {
            var vId = await vehicles.CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
            await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = custId, VehicleId = vId, BasTar = start, BitTar = start.AddDays(3), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m });
        }

        var rows = await sp.GetRequiredService<ReportService>().GetCustomerSegmentAsync();
        var r = Assert.Single(rows, x => x.CariId == custId);
        Assert.Equal(2, r.KiraSayisi);
        Assert.Equal(600m, r.ToplamCiro);   // 2 × (3 gün × 100)
        Assert.Equal("Standart", r.Segment); // 600 < 10000
    }

    /// <summary>
    /// FAZ-41 — segment satırının yeni alanları. BAĞIMSIZ ORACLE (elle kurulmuş senaryo):
    /// Cari A: 2 kira, 3 gün × 100 = 300 ve 2 gün × 100 = 200 → ciro 500, ort. kira bedeli 250.
    /// Cari B: 1 kira, 5 gün × 100 = 500 → ciro 500, ort. kira bedeli 500.
    /// Beklenen sayılar servisten DEĞİL bu kurulumdan türetilmiştir.
    /// </summary>
    [Fact]
    public async Task Segment_yeni_alanlar_ve_ortalama_kira_bedeli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cust = sp.GetRequiredService<CustomerService>();
        var vehicles = sp.GetRequiredService<VehicleService>();
        var rentals = sp.GetRequiredService<RentalService>();

        var a = await cust.CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Ahmet", Soyad = "Segment",
            Email = "ahmet@ornek.com", CepTel = "0555 111 22 33",
            DogumTarihi = new DateTimeOffset(1990, 6, 15, 0, 0, 0, TimeSpan.Zero)
        });
        var b = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Berk", Soyad = "Segment" });

        var start = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        var v1 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SG 01", Durum = VehicleStatus.Musait });
        var v2 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SG 02", Durum = VehicleStatus.Musait });
        var v3 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SG 03", Durum = VehicleStatus.Musait });

        // A'nın 1. kirası: 3 gün × 100 = 300
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = a, VehicleId = v1, BasTar = start, BitTar = start.AddDays(3), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Web" });
        // A'nın 2. kirası: 2 gün × 100 = 200
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = a, VehicleId = v2, BasTar = start.AddDays(30), BitTar = start.AddDays(32), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Acente" });
        // B: 5 gün × 100 = 500
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = b, VehicleId = v3, BasTar = start, BitTar = start.AddDays(5), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Web" });

        var reports = sp.GetRequiredService<ReportService>();
        var rows = await reports.GetCustomerSegmentAsync();

        var ra = Assert.Single(rows, x => x.CariId == a);
        Assert.Equal(2, ra.KiraSayisi);
        Assert.Equal(500m, ra.ToplamCiro);            // 300 + 200
        Assert.Equal(250m, ra.OrtalamaKiraBedeli);    // 500 / 2
        Assert.Equal("ahmet@ornek.com", ra.Mail);
        Assert.Equal("0555 111 22 33", ra.Tel);
        Assert.Equal(new DateTimeOffset(1990, 6, 15, 0, 0, 0, TimeSpan.Zero), ra.DogumTarihi);
        Assert.Equal(start, ra.IlkKiraZamani);          // en erken BasTar
        Assert.Equal(start.AddDays(30), ra.SonIslem);   // en geç BasTar
        Assert.Null(ra.OrtalamaKm);                   // DonusKm hiç girilmedi → "—" (0 DEĞİL)

        var rb = Assert.Single(rows, x => x.CariId == b);
        Assert.Equal(1, rb.KiraSayisi);
        Assert.Equal(500m, rb.OrtalamaKiraBedeli);    // 500 / 1
        Assert.Null(rb.Mail);
        Assert.Equal(0m, rb.HizmetBedeli);            // ek hizmet kalemi yok
    }

    /// <summary>
    /// FAZ-41 süzgeçleri. Kurulum: A'nın 2 kirası (Nisan "Web" + Mayıs "Acente"), B'nin 1 kirası (Nisan "Web").
    /// Beklenen satır sayıları elle sayılmıştır.
    /// </summary>
    [Fact]
    public async Task Segment_filtreleri_tarih_adet_kaynak_ofis()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cust = sp.GetRequiredService<CustomerService>();
        var vehicles = sp.GetRequiredService<VehicleService>();
        var rentals = sp.GetRequiredService<RentalService>();

        var a = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Filtre", Soyad = "A" });
        var b = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Filtre", Soyad = "B" });
        var nisan = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        var may = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);

        var v1 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 FL 01" });
        var v2 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 FL 02" });
        var v3 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 FL 03" });

        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = a, VehicleId = v1, BasTar = nisan, BitTar = nisan.AddDays(3), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Web", CikisOfisi = "Merkez Ofis" });
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = a, VehicleId = v2, BasTar = may, BitTar = may.AddDays(2), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Acente", CikisOfisi = "Havalimanı" });
        await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = b, VehicleId = v3, BasTar = nisan, BitTar = nisan.AddDays(5), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Web", CikisOfisi = "Merkez Ofis" });

        var reports = sp.GetRequiredService<ReportService>();

        // Süzgeçsiz: 2 müşteri.
        Assert.Equal(2, (await reports.GetCustomerSegmentAsync()).Count);

        // MinKiraSayisi=2 → yalnız A (B'nin 1 kirası var).
        var enAz2 = Assert.Single(await reports.GetCustomerSegmentAsync(new MusteriSegmentFilter { MinKiraSayisi = 2 }));
        Assert.Equal(a, enAz2.CariId);

        // Yalnız Mayıs penceresi → A'nın tek kirası (200 TL); B hiç görünmez.
        var mayRow = Assert.Single(await reports.GetCustomerSegmentAsync(new MusteriSegmentFilter
        { Bas = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), Bit = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.Equal(a, mayRow.CariId);
        Assert.Equal(1, mayRow.KiraSayisi);
        Assert.Equal(200m, mayRow.ToplamCiro);   // 2 gün × 100

        // Kaynak = Acente → yalnız A'nın Mayıs kirası.
        var agency = Assert.Single(await reports.GetCustomerSegmentAsync(new MusteriSegmentFilter { RezKaynak = "acente" }));
        Assert.Equal(a, agency.CariId);
        Assert.Equal(200m, agency.ToplamCiro);

        // Çıkış ofisi = Merkez Ofis → A (300) ve B (500), her biri 1 kira.
        var headOffice = await reports.GetCustomerSegmentAsync(new MusteriSegmentFilter { CikisOfis = "Merkez Ofis" });
        Assert.Equal(2, headOffice.Count);
        Assert.Equal(300m, headOffice.Single(x => x.CariId == a).ToplamCiro);
        Assert.Equal(500m, headOffice.Single(x => x.CariId == b).ToplamCiro);

        // Seçenek listesi FİLTRESİZ kümeden türer: her iki kaynak ve her iki ofis de görünür.
        var select = await reports.GetCustomerSegmentOptionsAsync();
        Assert.Contains("Web", select.Kaynaklar);
        Assert.Contains("Acente", select.Kaynaklar);
        Assert.Contains("Merkez Ofis", select.Ofisler);
        Assert.Contains("Havalimanı", select.Ofisler);
    }

    /// <summary>
    /// FAZ-41 — Ortalama KM ve Hizmet Bedeli. Km ve ek hizmet kalemleri kira formu/teslim akışından
    /// gelir; burada senaryo DOĞRUDAN kurulur (CustomerCrmTests deseni).
    ///
    /// <para>BAĞIMSIZ ORACLE: km farkları 300 (1000→1300) ve 400 (500→900), km'si eksik 3. kira
    /// ortalamaya GİRMEZ → (300+400)/2 = <b>350</b>. Ek hizmet kalemleri 120 + 80 = <b>200</b>.</para>
    /// </summary>
    [Fact]
    public async Task Segment_ortalama_km_ve_hizmet_bedeli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        Guid customerId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var veh = new Vehicle { Plaka = "34KM01", Durum = VehicleStatus.Musait };
            db.Vehicles.Add(veh);
            var c = new Customer { Tip = CustomerType.Bireysel, Ad = "Km", Soyad = "Müşterisi" };
            db.Customers.Add(c);
            customerId = c.Id;

            var k1 = new RentalContract
            {
                SozlesmeNo = "KS-KM1", MusteriId = c.Id, VehicleId = veh.Id, Durum = RentalStatus.Tamamlandi,
                BasTar = D(2026, 6, 1), BitTar = D(2026, 6, 3), GenelToplam = 300m, CikisKm = 1000, DonusKm = 1300
            };
            var k2 = new RentalContract
            {
                SozlesmeNo = "KS-KM2", MusteriId = c.Id, VehicleId = veh.Id, Durum = RentalStatus.Tamamlandi,
                BasTar = D(2026, 6, 10), BitTar = D(2026, 6, 12), GenelToplam = 200m, CikisKm = 500, DonusKm = 900
            };
            // Km'si girilmemiş kira: ortalamanın ne payına ne paydasına girer.
            var k3 = new RentalContract
            {
                SozlesmeNo = "KS-KM3", MusteriId = c.Id, VehicleId = veh.Id, Durum = RentalStatus.Kirada,
                BasTar = D(2026, 6, 20), BitTar = D(2026, 6, 22), GenelToplam = 100m
            };
            db.Rentals.AddRange(k1, k2, k3);

            db.RentalAddOns.Add(new RentalAddOn
            { RentalId = k1.Id, EkHizmetTanimId = Guid.NewGuid(), Ad = "Bebek Koltuğu", Miktar = 1m, BirimNetFiyat = 100m, KdvOrani = 0.20m, NetTutar = 100m, KdvTutar = 20m, Toplam = 120m });
            db.RentalAddOns.Add(new RentalAddOn
            { RentalId = k2.Id, EkHizmetTanimId = Guid.NewGuid(), Ad = "GPS", Miktar = 1m, BirimNetFiyat = 66.67m, KdvOrani = 0.20m, NetTutar = 66.67m, KdvTutar = 13.33m, Toplam = 80m });

            await db.SaveChangesAsync();
        }

        var rows = await scope.ServiceProvider.GetRequiredService<ReportService>().GetCustomerSegmentAsync();
        var r = Assert.Single(rows, x => x.CariId == customerId);
        Assert.Equal(3, r.KiraSayisi);
        Assert.Equal(350m, r.OrtalamaKm);      // (300 + 400) / 2 — km'siz kira paydaya girmez
        Assert.Equal(200m, r.HizmetBedeli);    // 120 + 80
    }

    /// <summary>Segment raporu tenant sınırını aşmaz (racar_app + RLS) — süzgeçli çağrıda da.</summary>
    [Fact]
    public async Task Segment_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp1 = s1.ServiceProvider;
            var c = await sp1.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "İzole", Soyad = "Müşteri" });
            var v = await sp1.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 IZ 01" });
            var start = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
            await sp1.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = c, VehicleId = v, BasTar = start, BitTar = start.AddDays(2), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Kaynak = "Web" });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var reports = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty(await reports.GetCustomerSegmentAsync(new MusteriSegmentFilter { RezKaynak = "Web" }));
        Assert.Empty((await reports.GetCustomerSegmentOptionsAsync()).Kaynaklar);
    }

    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Personel_calisma_baf_sayisi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var baf = sp.GetRequiredService<BafService>();
        var staffId = Guid.NewGuid();

        await baf.CreateAsync(new BafInput { PersonelId = staffId, VehicleId = Guid.NewGuid(), CikisKm = 100 });
        await baf.CreateAsync(new BafInput { PersonelId = staffId, VehicleId = Guid.NewGuid(), CikisKm = 200 });

        var rows = await sp.GetRequiredService<ReportService>().GetPersonnelWorkAsync();
        var r = Assert.Single(rows, x => x.PersonelId == staffId);
        Assert.Equal(2, r.TahsisSayisi);
    }
}
