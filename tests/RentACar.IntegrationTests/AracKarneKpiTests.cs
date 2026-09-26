using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç-karne PR3 — kurumsal KPI + amortisman/ekonomi bloğu. BAĞIMSIZ ORACLE (tümü elle):
/// sahiplik 31 gün (Oca 2025), kiralanan 3, servis 3 (ay sonuna açık kayıt → pencere kesimi), boş 25;
/// doluluk 3×100/31=9,68; fatura 200 brüt→net 166,67; RevPACD 166,67/31=5,38; ADR 166,67/3=55,56;
/// gider 150, km 300 → km-maliyet 0,50; net 16,67 → marj 10,00; AlimBedeli 1000 → ROI 1,67,
/// geri-ödeme ceil(1000/16,67)=60 ay; amortisman 1000−800=200 → ekonomik kâr −183,33; TCO 1150.
/// Satılmışta pencere satış tarihinde biter (90 gün). KPI'lar SAHİPLİK PENCERESİ (ömür boyu) metriğidir.
/// </summary>
[Collection("postgres")]
public sealed class AracKarneKpiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset FiloGiris = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FiloCikis = new(2025, 1, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset KiraBas = new(2025, 1, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Kpi_blogu_elle_oracle_ile_dogru()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34 KP 01", AlimBedeli = 1000m, IkinciElDeger = 800m,
            FiloGirisTarih = FiloGiris, FiloCikisTarih = FiloCikis
        });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Kpi", Soyad = "Cari" });

        // Kira: 10–12 Oca (2g×100=200 brüt), 300 km; tam zamanında dönüş (uzatma/fazla-km yok).
        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = KiraBas, BitTar = KiraBas.AddDays(2), GunlukUcret = 100m });
        await rentals.DeliverAsync(rental, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(rental, returnKm: 1300, returnFuel: 8, KiraBas.AddDays(2));
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental); // 200 brüt → net 166,67

        // Servis: 29 Oca girişli AÇIK kayıt (çıkışsız → bugüne dek; pencere 29-30-31 = 3 gün keser).
        await sp.GetRequiredService<ServiceRecordService>().CreateAsync(new ServiceRecordInput
        {
            VehicleId = vehicle, Tip = ServiceType.Periyodik, GirisKm = 1300,
            GirisTarihi = new DateTimeOffset(2025, 1, 29, 8, 0, 0, TimeSpan.Zero),
            Lines = [new ServiceLineInput { Aciklama = "Bakım", Tutar = 75m }]
        });

        // Gider: net 150 (KDV 0).
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = vehicle, NetTutar = 150m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(vehicle);
        var kpi = k!.Kpi;

        Assert.Equal(31, kpi.SahiplikGun);                    // 1–31 Oca kapsayıcı
        Assert.Equal(3, kpi.KiralananGun);                    // 10,11,12
        Assert.Equal(3, kpi.ServisGun);                       // 29,30,31 (pencere kesimi)
        Assert.Equal(25, kpi.BosGun);                         // 31−3−3
        Assert.Equal(9.68m, kpi.DolulukYuzde);                // 300/31
        Assert.Equal(166.67m, k.ToplamGelir);                 // 200/1,2
        Assert.Equal(5.38m, kpi.RevPacd);                     // 166,67/31
        Assert.Equal(55.56m, kpi.Adr);                        // 166,67/3
        Assert.Equal(300, kpi.ToplamKatedilenKm);
        Assert.Equal(0.50m, kpi.KmBasinaMaliyet);             // 150/300
        Assert.Equal(10.00m, kpi.NetMarjYuzde);               // 16,67×100/166,67
        Assert.Equal(1.67m, kpi.RoiYuzde);                    // 16,67×100/1000
        Assert.Equal(60, kpi.GeriOdemeAy);                    // ceil(1000/(16,67/1))
        Assert.Equal(1150m, kpi.Tco);                          // 1000+150
        Assert.Equal(200m, kpi.GerceklesenAmortisman);        // 1000−800
        Assert.Equal(200m, kpi.AylikAmortisman);              // 200/1 ay
        Assert.Equal(-183.33m, kpi.EkonomikKar);              // 16,67−200
        Assert.Equal(1, kpi.KiraSayisi);

        // Amortisman/başabaş modeli (MaliyetHesapService; elle): residual 0,8 → 800; net amortisman 200;
        // aylık gider 150 → toplam maliyet 350; başabaş (1 ay) 350.
        Assert.NotNull(k.MaliyetModel);
        Assert.Equal(800m, k.MaliyetModel!.ResidualDeger);
        Assert.Equal(200m, k.MaliyetModel.NetAmortisman);
        Assert.Equal(350m, k.MaliyetModel.ToplamMaliyet);
        Assert.Equal(350m, k.MaliyetModel.BasaBasAylik);
    }

    [Fact]
    public async Task GeriOdeme_kurus_net_ile_tasmaz() // adversarial F1 (High): OverflowException → 500
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // 25M TL araç + 1 KURUŞ net kâr: eski kod (int)Math.Ceiling ile taşıyordu.
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 KP 04", AlimBedeli = 25_000_000m, FiloGirisTarih = FiloGiris, FiloCikisTarih = FiloCikis });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Tasma", Soyad = "Cari" });

        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = KiraBas, BitTar = KiraBas.AddDays(2), GunlukUcret = 100m });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental);   // net 166,67
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = vehicle, NetTutar = 166.66m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(vehicle); // exception YOK
        Assert.Equal(0.01m, k!.ToplamGelir - k.ToplamGider);   // 1 kuruş net (elle)
        Assert.Null(k.Kpi.GeriOdemeAy);                        // ceil(25M/0,01)=2,5e9 ay → >1200 → null
    }

    [Fact]
    public async Task Satilmis_aracta_ekonomik_kar_kalintiyi_cift_saymaz() // adversarial F3 (Medium)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // Alım 1000, tahmini kalıntı 800, TAM 800'e satıldı → gerçek ekonomi 800−1000 = −200 ZARAR.
        // Eski kod: satış geliri + tahmini kalıntı birlikte → +600 kâr / +%80 ROI gösteriyordu.
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 KP 05", AlimBedeli = 1000m, IkinciElDeger = 800m, FiloGirisTarih = FiloGiris });
        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        {
            VehicleId = vehicle, AliciCariId = Guid.NewGuid(), SatisNet = 800m, KdvOrani = 0m,
            Doviz = "TRY", Kur = 1m, Tarih = new DateTimeOffset(2025, 3, 31, 12, 0, 0, TimeSpan.Zero)
        });

        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(vehicle);
        Assert.Equal(800m, k!.ToplamGelir);                    // satış geliri defterde
        Assert.Equal(1000m, k.Kpi.GerceklesenAmortisman);      // kalıntı REALİZE edildi → tam alım düşülür
        Assert.Equal(-200m, k.Kpi.EkonomikKar);                // 800 − 1000 (elle; +600 DEĞİL)
        Assert.Equal(-20.00m, k.Kpi.RoiYuzde);                 // kapanış ROI'si (satış dahil, alım düşülmüş)
    }

    [Fact]
    public async Task Kpi_donem_filtresinden_bagimsiz() // adversarial F2 (Medium): karışık-payda sızıntısı
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 KP 06", AlimBedeli = 1000m, FiloGirisTarih = FiloGiris, FiloCikisTarih = FiloCikis });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Pencere", Soyad = "Cari" });

        // 2024'te rücu 300, 2026'da (bugün) gider 150 → P&L pencereye göre değişir, KPI DEĞİŞMEZ.
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var sid = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = vehicle, Tip = ServiceType.Ariza, GirisKm = 0, HasarSorumlu = DamageResponsible.Musteri,
            KusurOrani = 0.5m, Lines = [new ServiceLineInput { Aciklama = "X", Tutar = 600m }]
        });
        await svc.StartAsync(sid);
        await svc.CompleteAsync(sid, pickupKm: 10);
        await svc.ReflectAsync(sid, cari, date: new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = vehicle, NetTutar = 150m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit });

        var rs = sp.GetRequiredService<ReportService>();
        var tam = await rs.GetVehicleScorecardAsync(vehicle);
        var pencere = await rs.GetVehicleScorecardAsync(vehicle,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Assert.Equal(300m, pencere!.ToplamGelir);              // P&L pencereli (gider 2026 → dışarıda)
        Assert.Equal(0m, pencere.ToplamGider);
        Assert.Equal(tam!.Kpi, pencere.Kpi);                   // KPI bloğu record-eşit: tümü ömür-boyu
        Assert.Equal(1150m, pencere.Kpi.Tco);                  // 1000 + 150 (pencere DIŞI gider dahil)
    }

    [Fact]
    public async Task Gec_donus_cakismasinda_doluluk_100u_asmaz() // adversarial F4 (Low)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // Sahiplik 10–15 Oca (6 gün). Kira A 10→12 planlı ama 15'inde döndü (geç, 6 efektif gün);
        // kira B 13→14 (2 gün; A'nın planlı aralığıyla çakışmadığından oluşturulabildi). 8 > 6 → cap 100.
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34 KP 07",
            FiloGirisTarih = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
            FiloCikisTarih = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero)
        });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Cakisma", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();

        var a = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = KiraBas, BitTar = KiraBas.AddDays(2), GunlukUcret = 100m });
        var b = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = KiraBas.AddDays(3), BitTar = KiraBas.AddDays(4), GunlukUcret = 100m });
        await rentals.DeliverAsync(a, pickupKm: 0, pickupFuel: 8);
        await rentals.ReturnAsync(a, returnKm: 100, returnFuel: 8, KiraBas.AddDays(5)); // 15 Oca — GEÇ

        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(vehicle);
        Assert.Equal(6, k!.Kpi.SahiplikGun);
        Assert.Equal(8, k.Kpi.KiralananGun);                   // 6 (A efektif) + 2 (B) — veri gerçeği
        Assert.Equal(100.00m, k.Kpi.DolulukYuzde);             // 133,33 DEĞİL — cap
        _ = b;
    }

    [Fact]
    public async Task Ikinci_el_sifir_veya_negatif_hizali()   // adversarial F5 (Low)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();

        // IkinciEl=0 → "veri yok": KPI amortismanı NULL (eski: 1000 tam amortisman — modelin 0.30
        // varsayımıyla aynı sayfada çelişiyordu); model varsayılan 0.30 residual kullanır.
        var v0 = await veh.CreateAsync(new VehicleInput
        { Plaka = "34 KP 08", AlimBedeli = 1000m, IkinciElDeger = 0m, FiloGirisTarih = FiloGiris });
        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(v0);
        Assert.Null(k!.Kpi.GerceklesenAmortisman);
        Assert.Null(k.Kpi.EkonomikKar);
        Assert.Equal(300m, k.MaliyetModel!.ResidualDeger);     // 1000 × 0.30 varsayılan

        // Negatif bedeller artık girişte reddedilir.
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            veh.CreateAsync(new VehicleInput { Plaka = "34 KP 09", IkinciElDeger = -50m }));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            veh.CreateAsync(new VehicleInput { Plaka = "34 KP 10", AlimBedeli = -1m }));
    }

    [Fact]
    public async Task Verisiz_aracta_kpi_null_guvenli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // Ne alım bedeli, ne filo tarihi, ne kira — hiçbir KPI patlamamalı, oranlar null.
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KP 02" });

        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(vehicle);
        var kpi = k!.Kpi;
        Assert.Equal(0, kpi.SahiplikGun);
        Assert.Null(kpi.DolulukYuzde);
        Assert.Null(kpi.RevPacd);
        Assert.Null(kpi.Adr);
        Assert.Null(kpi.KmBasinaMaliyet);
        Assert.Null(kpi.NetMarjYuzde);
        Assert.Null(kpi.RoiYuzde);
        Assert.Null(kpi.GeriOdemeAy);
        Assert.Null(kpi.GerceklesenAmortisman);
        Assert.Null(kpi.EkonomikKar);
        Assert.Equal(0m, kpi.Tco);
        Assert.Null(k.MaliyetModel);                          // AlimBedeli yok → model çağrılmaz
    }

    [Fact]
    public async Task Satilmis_aracta_pencere_satis_tarihinde_biter()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // FiloCikis YOK ama satıldı → W_bit = satış tarihi (bugüne uzamaz). 1 Oca → 31 Mar = 90 gün.
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 KP 03", FiloGirisTarih = FiloGiris });
        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        {
            VehicleId = vehicle, AliciCariId = Guid.NewGuid(), SatisNet = 5000m, KdvOrani = 0m,
            Doviz = "TRY", Kur = 1m, Tarih = new DateTimeOffset(2025, 3, 31, 12, 0, 0, TimeSpan.Zero)
        });

        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(vehicle);
        Assert.Equal(90, k!.Kpi.SahiplikGun);                 // 31+28+31 (elle; bugüne UZAMADI)
        Assert.Equal(5000m, k.ToplamGelir);                   // satış geliri (AracSatis ataması)
        Assert.Equal(55.56m, k.Kpi.RevPacd);                  // 5000/90
        Assert.Equal("Araç Satışı", Assert.Single(k.GelirKaynak).Kategori);
    }
}
