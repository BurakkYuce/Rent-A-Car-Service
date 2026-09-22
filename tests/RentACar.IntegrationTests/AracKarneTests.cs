using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç-karne PR2 — tek araç 360° P&L çekirdeği (GetAracKarneAsync). P&L DEFTERDEN, olaylar kaynak
/// varlıktan (bilgi amaçlı, DeftereYansir bayraklı). BAĞIMSIZ ORACLE (elle):
/// 2024: servis rücu 600×0.5=300 gelir + araç gideri 100; 2025: rücu 400×0.5=200 + MTV 50
/// → toplam 500/150/350; yıllık {2024:(300,100,200), 2025:(200,50,150)}.
/// Fark+iade: base 300 brüt→250 net + fark 500 − iade 500 = 250. Çift-sayım: servis 400 deftersiz → gider 0.
/// PARİTE KİLİDİ: karne toplamları = o aracın GetKarlilikAsync satırı (iki yol drifte karşı bağlı).
/// </summary>
[Collection("postgres")]
public sealed class AracKarneTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Y2024 = new(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Y2025 = new(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    /// <summary>Tamamlanmış servis kaydı (maliyet, kusur 0.5, sorumlu Müşteri) — yansıtma çağıranın işi.</summary>
    private static async Task<Guid> ServisKurAsync(IServiceProvider sp, Guid vehicle, decimal maliyet)
    {
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = vehicle, Tip = ServisTipi.Ariza, GirisKm = 0,
            HasarSorumlu = HasarSorumlu.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Onarım", Tutar = maliyet }]
        });
        await svc.BaslatAsync(id);
        await svc.TamamlaAsync(id, cikisKm: 100);
        return id;
    }

    [Fact]
    public async Task Yillik_pnl_kirilim_ve_karlilik_paritesi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Karne", Soyad = "Cari" });
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var reg = sp.GetRequiredService<RegulationService>();

        // 2024: rücu geliri 600×0.5=300 + araç gideri net 100 (elle).
        await svc.YansitAsync(await ServisKurAsync(sp, vehicle, 600m), cari, tarih: Y2024);
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = vehicle, NetTutar = 100m, KdvOrani = 0m, Tarih = Y2024, OdemeYontemi = OdemeYontemi.Nakit });

        // 2025: rücu geliri 400×0.5=200 + MTV 50 ödendi (elle).
        await svc.YansitAsync(await ServisKurAsync(sp, vehicle, 400m), cari, tarih: Y2025);
        var mtv = await reg.AddMtvAsync(vehicle, "2025-1", 50m, Y2025);
        await reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa, odemeTarih: Y2025);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetAracKarneAsync(vehicle);

        Assert.NotNull(k);
        Assert.Equal("34KR01", k!.Header.Plaka);          // plaka normalize
        Assert.Equal(500m, k.ToplamGelir);                 // 300+200 elle
        Assert.Equal(150m, k.ToplamGider);                 // 100+50 elle
        Assert.Equal(350m, k.ToplamNetKar);

        // Yıllık kırılım (UTC yıl).
        Assert.Equal(2, k.YillikPnl.Count);
        Assert.Equal(new AracYilPnlRow(2024, 300m, 100m, 200m), k.YillikPnl[0]);
        Assert.Equal(new AracYilPnlRow(2025, 200m, 50m, 150m), k.YillikPnl[1]);

        // Kırılımlar (% of revenue: 500 üzerinden).
        var kaynak = Assert.Single(k.GelirKaynak);
        Assert.Equal(("Servis Yansıtma", 500m, 100m), (kaynak.Kategori, kaynak.Tutar, kaynak.YuzdeGelir));
        Assert.Equal(2, k.GiderKategori.Count);
        Assert.Contains(k.GiderKategori, g => g.Kategori == "Araç Gideri" && g.Tutar == 100m && g.YuzdeGelir == 20m);
        Assert.Contains(k.GiderKategori, g => g.Kategori == "MTV" && g.Tutar == 50m && g.YuzdeGelir == 10m);

        // Olaylar: servis DeftereYansir=false (maliyet deftersiz), MTV/Gider true.
        Assert.Contains(k.Olaylar, o => o.Tur.StartsWith("Servis") && o.Tutar == 600m && !o.DeftereYansir);
        Assert.Contains(k.Olaylar, o => o.Tur == "MTV" && o.Tutar == 50m && o.DeftereYansir);
        Assert.Contains(k.Olaylar, o => o.Tur.StartsWith("Gider") && o.Tutar == 100m && o.DeftereYansir);

        // PARİTE KİLİDİ: karne toplamları = Karlilik satırı (aynı atıf kuralları — drift'e karşı kalıcı bağ).
        var satir = Assert.Single((await rs.GetKarlilikAsync()).Satirlar);
        Assert.Equal(vehicle, satir.VehicleId);
        Assert.Equal(satir.Gelir, k.ToplamGelir);
        Assert.Equal(satir.Gider, k.ToplamGider);
    }

    [Fact]
    public async Task Servis_maliyeti_cift_sayilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 02" });
        var rs = sp.GetRequiredService<ReportService>();

        // Servis maliyeti 400 — mali belge DEĞİL → defter P&L'inde YOK, yalnız olay (bilgi, false).
        await ServisKurAsync(sp, vehicle, 400m);
        var k1 = await rs.GetAracKarneAsync(vehicle);
        Assert.Equal(0m, k1!.ToplamGider);
        Assert.Empty(k1.GiderKategori);
        Assert.Contains(k1.Olaylar, o => o.Tur.StartsWith("Servis") && o.Tutar == 400m && !o.DeftereYansir);

        // Aynı maliyet ayrıca Gider dilimi olarak girilirse: BİR kez sayılır (400, 800 DEĞİL); iki ayrı olay.
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Arac, VehicleId = vehicle, NetTutar = 400m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.Nakit });
        var k2 = await rs.GetAracKarneAsync(vehicle);
        Assert.Equal(400m, k2!.ToplamGider);
        Assert.Contains(k2.Olaylar, o => o.Tur.StartsWith("Servis") && !o.DeftereYansir);
        Assert.Contains(k2.Olaylar, o => o.Tur.StartsWith("Gider") && o.DeftereYansir);
    }

    [Fact]
    public async Task Fark_ve_iade_karnede_netlesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 03" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Fark", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();

        // 3g×100=300 brüt base (net 250) + dönüş 300 aşım×2=600 fark (net 500) + fark iadesi (−500) = 250.
        var rental = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = vehicle, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 2m
        });
        await invoices.CreateFromRentalAsync(rental);
        await rentals.DeliverAsync(rental, cikisKm: 1000, cikisYakit: 8);
        await rentals.ReturnAsync(rental, donusKm: 1600, donusYakit: 8, Bas.AddDays(3));
        var farkId = await invoices.CreateFromRentalAsync(rental);
        await invoices.CreateIadeAsync(farkId);

        var rs = sp.GetRequiredService<ReportService>();
        var k = await rs.GetAracKarneAsync(vehicle);
        Assert.Equal(250m, k!.ToplamGelir);                         // 250+500−500 elle
        var kaynak = Assert.Single(k.GelirKaynak);
        Assert.Equal("Kira/Fatura", kaynak.Kategori);
        Assert.Equal(250m, kaynak.Tutar);
        Assert.Contains(k.Olaylar, o => o.Tur == "Kira" && o.DeftereYansir);

        // Parite: Karlilik satırıyla aynı.
        var satir = Assert.Single((await rs.GetKarlilikAsync()).Satirlar);
        Assert.Equal(satir.Gelir, k.ToplamGelir);
    }

    [Fact]
    public async Task Tarih_filtresi_pnl_ve_olaylara_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 04" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Filtre", Soyad = "Cari" });
        var svc = sp.GetRequiredService<ServiceRecordService>();

        // 2024 rücu 300 + 2025 rücu 200; pencere 2024 → yalnız 300 ve yalnız 2024 olayları.
        await svc.YansitAsync(await ServisKurAsync(sp, vehicle, 600m), cari, tarih: Y2024);
        await svc.YansitAsync(await ServisKurAsync(sp, vehicle, 400m), cari, tarih: Y2025);

        var k = await sp.GetRequiredService<ReportService>().GetAracKarneAsync(vehicle,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero));
        Assert.Equal(300m, k!.ToplamGelir);
        var yil = Assert.Single(k.YillikPnl);
        Assert.Equal(2024, yil.Yil);
        Assert.All(k.Olaylar, o => Assert.True(o.Tarih.Year == 2024));
    }

    [Fact]
    public async Task Capraz_arac_ceza_celiskisi_paritede_tek_araca()
    {
        // Adversarial parite avı: ceza VehicleId=A ama RentalId=B'nin kirası → filo raporu A'ya yazar
        // (VehicleId önceliği). Karne A dahil etmeli, karne B HARİÇ tutmalı; toplam çift sayılmamalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehA = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 08" });
        var vehB = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 09" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Celiski", Soyad = "Cari" });
        var rentalB = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehB, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m });

        var pen = sp.GetRequiredService<RentACar.Application.Penalties.PenaltyService>();
        var celiskili = await pen.CreateAsync(new RentACar.Application.Penalties.PenaltyInput
        { CezaTuru = "Hız", VehicleId = vehA, RentalId = rentalB, CariId = cari, Tutar = 80m }); // A kazanır
        await pen.YansitAsync(celiskili);

        var rs = sp.GetRequiredService<ReportService>();
        var kA = await rs.GetAracKarneAsync(vehA);
        var kB = await rs.GetAracKarneAsync(vehB);
        Assert.Equal(80m, kA!.ToplamGelir);       // VehicleId önceliği: A'ya
        Assert.Equal(0m, kB!.ToplamGelir);        // B'ye SIZMAZ (çift sayım yok)

        // Filo raporuyla parite: A satırı 80, B satırı yok/0.
        var filo = await rs.GetKarlilikAsync();
        Assert.Equal(80m, filo.Satirlar.Single(r => r.VehicleId == vehA).Gelir);
        Assert.DoesNotContain(filo.Satirlar, r => r.VehicleId == vehB && r.Gelir != 0m);
        Assert.Equal(80m, filo.ToplamGelir);
    }

    [Fact]
    public async Task Negatif_donem_gelirinde_yuzdeler_null()
    {
        // Adversarial F1: pencere-net'i negatifse % of revenue yanıltır → null olmalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 06" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Neg", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();

        // Fatura BUGÜN (pencere dışı), iadesi 10 gün önce tarihli (pencere içi) → pencere net −250.
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m });
        var inv = await invoices.CreateFromRentalAsync(rental);
        await invoices.CreateIadeAsync(inv, tarih: DateTimeOffset.UtcNow.AddDays(-10));

        var k = await sp.GetRequiredService<ReportService>().GetAracKarneAsync(vehicle,
            DateTimeOffset.UtcNow.AddDays(-15), DateTimeOffset.UtcNow.AddDays(-5));
        Assert.Equal(-250m, k!.ToplamGelir);                       // elle: yalnız iade pencerede
        Assert.All(k.GelirKaynak, r => Assert.Null(r.YuzdeGelir)); // yüzde YOK (yanıltıcı olurdu)
    }

    [Fact]
    public async Task Kira_olayi_bayragi_faturalanmis_mi_demek()
    {
        // Adversarial F3: DeftereYansir "iptal değil" değil "parası defterde (fatura kesilmiş)" demek.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 07" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Bayrak", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();

        // Kira 1: faturalandı SONRA iptal edildi → fatura defterde kalır (immutable) → bayrak TRUE.
        // F4.1 adversarial M3: servis artık faturalı kirayı İPTAL ETMİYOR (önce iade faturası). Bu durum ESKİ
        // veride vardır ve karne onu doğru göstermeli → durum doğrudan yazılarak kurulur (eski kayıt benzetimi).
        var r1 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(r1);
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() => rentals.CancelAsync(r1));
        await using (var db = await sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<RentACar.Infrastructure.Persistence.AppDbContext>>().CreateDbContextAsync())
        {
            var kira = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Rentals, x => x.Id == r1);
            kira.Durum = RentalStatus.Iptal;
            await db.SaveChangesAsync();
        }

        // Kira 2: hiç faturalanmadı → parası defterde YOK → bayrak FALSE.
        var r2 = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = vehicle, BasTar = Bas.AddDays(5), BitTar = Bas.AddDays(7), GunlukUcret = 100m });

        var k = await sp.GetRequiredService<ReportService>().GetAracKarneAsync(vehicle);
        var kiraOlaylari = k!.Olaylar.Where(o => o.Tur == "Kira").ToList();
        Assert.Equal(2, kiraOlaylari.Count);
        Assert.True(kiraOlaylari.Single(o => o.Aciklama.Contains("Iptal")).DeftereYansir);   // faturalı-iptal
        Assert.False(kiraOlaylari.Single(o => !o.Aciklama.Contains("Iptal")).DeftereYansir); // faturasız
        Assert.Equal(250m, k.ToplamGelir);   // iptal edilse de kesilmiş faturanın geliri defterde (parite korunur)
        _ = r2;
    }

    [Fact]
    public async Task Olmayan_veya_baska_tenant_araci_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        Guid vehicle;
        using (var scopeA = host.ScopeFor(tenantA))
            vehicle = await scopeA.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 KR 05" });

        using var scopeB = host.ScopeFor(Guid.NewGuid());
        var rs = scopeB.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Null(await rs.GetAracKarneAsync(vehicle));           // çapraz tenant (racar_app + RLS) → 404
        Assert.Null(await rs.GetAracKarneAsync(Guid.NewGuid()));    // hiç yok → 404
    }
}
