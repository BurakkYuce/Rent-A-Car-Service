using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-18 — Araç Satış + BAF alan/filtre derinliği.
///
/// BAĞIMSIZ ORACLE (elle kurulan senaryolardan, servis kodundan DEĞİL):
///   • Satış filtresi: 3 satış seedlenir → SatisiVerildi=true olan TEK satış (ST-…) döner.
///   • Ofis filtresi: aracı "Merkez" şubesinde olan 1 satış → 1 sonuç; "Ankara" → 0.
///   • Geçen Süre: 2026-01-01 satışı, bugün 2026-01-11 → 10 gün.
///   • BAF lokasyon: (A,A) → Aynı Ofis kümesinde 1; (A,B) → Farklı Ofis kümesinde 1;
///     dönüş şubesi BOŞ olan üçüncü kayıt İKİ kümede de YOK.
///   • PARA REGRESYONU: alım 1000 / kalıntı 800 / satış net 800 aracın karne rakamları
///     (gelir 800, gerçekleşen amortisman 1000, ekonomik kâr −200, ROI −%20) yeni BİLGİ
///     alanları uçuk değerlerle doldurulduğunda DEĞİŞMEZ — bu sayılar AracKarneKpiTests'teki
///     mevcut senaryodan birebir alınmıştır (iki yol drifte karşı bağlı).
/// </summary>
[Collection("postgres")]
public sealed class SatisBafDerinlikTests(PostgresFixture fx)
{
    // CI-vs-lokal tick farkı: DateTimeOffset round-trip eşitliği için tarih tabanı TAM SANİYEYE hizalı.
    private static DateTimeOffset Saniye(DateTimeOffset t)
        => t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));

    private static readonly DateTimeOffset Bugun =
        Saniye(new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), TimeSpan.Zero));

    private static async Task<Guid> AracAsync(IServiceScope scope, string plaka, string? sube = null)
        => await scope.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Sube = sube });

    // ---------------------------------------------------------------- Bölüm A: Araç Satış

    [Fact]
    public async Task Satis_yeni_bilgi_alanlari_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var arac = await AracAsync(scope, "34 RT 01");
        var ihale = Saniye(Bugun.AddDays(-5));
        var noter = Saniye(Bugun.AddDays(-2));

        var id = await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = arac, AliciCariId = Guid.NewGuid(), SatisNet = 100_000m, KdvOrani = 0.20m,
            Doviz = "TRY", Kur = 1m, Tarih = Bugun,
            KirayaVerme = true, IlanKm = 55_000, SatisKm = 57_500, ListeDoviz = "usd",
            SatisNoktasi = "Merkez Galeri", UygulananKampanya = "YAZ-2026",
            IhaleFirmasi = " Alfa İhale ", IhaleTarihi = ihale, IhaleSayisi = "2026/144",
            NoterSatisTarihi = noter, SatisiVerildi = true, YevmiyeNumarasi = "YEV-9001",
            Aciklama2 = "ikinci açıklama"
        });

        var s = await sales.GetAsync(id);
        Assert.NotNull(s);
        Assert.True(s!.KirayaVerme);
        Assert.Equal(55_000, s.IlanKm);
        Assert.Equal(57_500, s.SatisKm);           // İlan KM ile Satış KM AYRI alanlar (karışmıyor)
        Assert.Equal("USD", s.ListeDoviz);         // normalize: büyük harf
        Assert.Equal("TRY", s.Currency);           // satışın KENDİ dövizi etkilenmedi
        Assert.Equal("Merkez Galeri", s.SatisNoktasi);
        Assert.Equal("YAZ-2026", s.UygulananKampanya);
        Assert.Equal("Alfa İhale", s.IhaleFirmasi); // trim
        Assert.Equal(ihale, s.IhaleTarihi);
        Assert.Equal("2026/144", s.IhaleSayisi);
        Assert.Equal(noter, s.NoterSatisTarihi);
        Assert.True(s.SatisiVerildi);
        Assert.Equal("YEV-9001", s.YevmiyeNumarasi);
        Assert.Equal("ikinci açıklama", s.Aciklama2);

        // Bilgi alanları PARA zincirine dokunmadı: net 100.000 @0,20 → KDV 20.000, brüt 120.000 (elle).
        Assert.Equal(20_000m, s.KdvTutar);
        Assert.Equal(120_000m, s.GenelToplam);
        Assert.Equal(SaleStatus.Tamamlandi, s.Durum);   // SatisiVerildi bayrağı Durum'u DEĞİŞTİRMEZ
    }

    [Fact]
    public async Task Satis_bilgi_alani_dogrulamalari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var arac = await AracAsync(scope, "34 RT 02");

        VehicleSaleInput Girdi() => new()
        { VehicleId = arac, AliciCariId = Guid.NewGuid(), SatisNet = 1000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m };

        var g1 = Girdi(); g1.IlanKm = -1;
        Assert.Contains("İlan KM", (await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(g1))).Message);

        var g2 = Girdi(); g2.ListeDoviz = "DOLAR";   // 3 harften uzun → kolon sınırında 500 yerine temiz red
        Assert.Contains("3 harfli", (await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(g2))).Message);
    }

    [Fact]
    public async Task Satis_filtresi_sonucu_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();

        // 3 satış (elle): A=devir verildi + Merkez aracı + bugün; B=devir yok + Ankara; C=devir yok + şubesiz, 30 gün önce.
        var aracA = await AracAsync(scope, "34 FL 01", sube: "Merkez");
        var aracB = await AracAsync(scope, "34 FL 02", sube: "Ankara");
        var aracC = await AracAsync(scope, "34 FL 03");
        var cariA = Guid.NewGuid();

        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = aracA, AliciCariId = cariA, SatisNet = 10m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, Tarih = Bugun, SatisiVerildi = true });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = aracB, AliciCariId = Guid.NewGuid(), SatisNet = 20m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, Tarih = Bugun });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = aracC, AliciCariId = Guid.NewGuid(), SatisNet = 30m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, Tarih = Bugun.AddDays(-30) });

        Assert.Equal(3, (await sales.SearchAsync()).Count);                                   // filtresiz: hepsi

        var devir = await sales.SearchAsync(new VehicleSaleFilter { SatisiVerildi = true });
        Assert.Single(devir);                                                                 // ORACLE: 3 satıştan 1'i
        Assert.Equal(aracA, devir[0].VehicleId);

        Assert.Equal(2, (await sales.SearchAsync(new VehicleSaleFilter { SatisiVerildi = false })).Count);

        var ofis = await sales.SearchAsync(new VehicleSaleFilter { Ofis = "Merkez" });
        Assert.Single(ofis);                                                                  // aracın şubesinden
        Assert.Equal(aracA, ofis[0].VehicleId);
        Assert.Empty(await sales.SearchAsync(new VehicleSaleFilter { Ofis = "İzmir" }));

        var plaka = await sales.SearchAsync(new VehicleSaleFilter { Plaka = "34 FL 02" });     // boşluklu yazım da bulmalı
        Assert.Single(plaka);
        Assert.Equal(aracB, plaka[0].VehicleId);

        Assert.Single(await sales.SearchAsync(new VehicleSaleFilter { AliciCariId = cariA }));

        var tarih = await sales.SearchAsync(new VehicleSaleFilter { Bas = Bugun.AddDays(-1) });
        Assert.Equal(2, tarih.Count);                                                         // 30 gün öncesi düşer
        Assert.Equal(3, (await sales.SearchAsync(new VehicleSaleFilter { Bit = Bugun })).Count);

        Assert.Equal(3, (await sales.SearchAsync(new VehicleSaleFilter { Durum = SaleStatus.Tamamlandi })).Count);
        Assert.Empty(await sales.SearchAsync(new VehicleSaleFilter { Durum = SaleStatus.Iptal }));
    }

    [Fact]
    public void Gecen_gun_hesabi_sabit_tarihten()
    {
        var satis = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var simdi = new DateTimeOffset(2026, 1, 11, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(10, SaleCalculation.ElapsedDays(satis, simdi));       // ORACLE: 1 Ocak → 11 Ocak = 10 gün
        Assert.Equal(0, SaleCalculation.ElapsedDays(simdi, simdi));
        Assert.Equal(-2, SaleCalculation.ElapsedDays(simdi.AddDays(2), simdi)); // gelecek tarih gizlenmez
    }

    /// <summary>
    /// KIRILGAN PARA REGRESYONU (KARARLAR.md genel politikası): yeni BİLGİ alanları uçuk değerlerle
    /// doldurulduğunda defter ve karne/karlılık rakamları DEĞİŞMEZ. Beklenen değerler AracKarneKpiTests'teki
    /// elle kurulmuş senaryonun aynısıdır — bu alanlardan biri bir gün P&amp;L'e toplanırsa test kırmızıya döner.
    /// </summary>
    [Fact]
    public async Task Yeni_bilgi_alanlari_deftere_ve_rapora_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var filoGiris = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var satisTarih = new DateTimeOffset(2025, 3, 31, 12, 0, 0, TimeSpan.Zero);

        // Alım 1000, tahmini kalıntı 800, TAM 800'e satıldı → gerçek ekonomi 800−1000 = −200 (elle).
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 SZ 01", AlimBedeli = 1000m, IkinciElDeger = 800m, FiloGirisTarih = filoGiris });

        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        {
            VehicleId = arac, AliciCariId = Guid.NewGuid(), SatisNet = 800m, KdvOrani = 0m,
            Doviz = "TRY", Kur = 1m, Tarih = satisTarih,
            // UÇUK bilgi alanları — hiçbiri deftere/rapora girmemeli.
            HedefFiyat = 9_999_999m, IlanKm = 999_999, SatisKm = 999_999, KirayaVerme = true,
            ListeDoviz = "EUR", SatisNoktasi = "Uçuk Nokta", UygulananKampanya = "SIZINTI-TESTI",
            IhaleFirmasi = "Uçuk İhale", IhaleSayisi = "9999/9999", SatisiVerildi = true,
            YevmiyeNumarasi = "9999999", Aciklama2 = new string('x', 400)
        });

        // 1) Defter: yalnız 3 satır ve tam olarak 800 (KDV 0) — bilgi alanları hiçbir satır üretmedi.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var entries = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => e.SourceType == "AracSatis").ToListAsync();
            Assert.Equal(3, entries.Count);   // Borç Cari + Alacak Gelir + Alacak KDV (KDV oranı 0 → tutar 0)
            Assert.Equal(800m, entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
            Assert.Equal(800m, entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));
            Assert.Equal(800m, entries.Single(e => e.AccountType == LedgerAccountType.Gelir).Amount.AmountInBase);
        }

        // 2) Araç Karnesi — AracKarneKpiTests'teki senaryonun BİREBİR aynı sayıları.
        var k = await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(arac);
        Assert.Equal(800m, k!.ToplamGelir);
        Assert.Equal(0m, k.ToplamGider);
        Assert.Equal(1000m, k.Kpi.GerceklesenAmortisman);
        Assert.Equal(-200m, k.Kpi.EkonomikKar);
        Assert.Equal(-20.00m, k.Kpi.RoiYuzde);

        // 3) Karlılık raporu — aynı araç satırı; HedefFiyat 9.999.999 hiçbir toplama girmedi.
        var karlilik = await sp.GetRequiredService<ReportService>().GetProfitabilityAsync();
        Assert.Equal(800m, karlilik.ToplamGelir);
        Assert.Equal(0m, karlilik.ToplamGider);
        Assert.Equal(800m, karlilik.ToplamNetKar);
    }

    [Fact]
    public async Task Satis_arama_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            var arac = await AracAsync(a, "34 TZ 01");
            await a.ServiceProvider.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
            { VehicleId = arac, AliciCariId = Guid.NewGuid(), SatisNet = 100m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, SatisiVerildi = true });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        // racar_app ile bağlanan fixture: RLS + query filter → başka tenant'ın satışı GÖRÜNMEZ.
        Assert.Empty(await b.ServiceProvider.GetRequiredService<VehicleSaleService>()
            .SearchAsync(new VehicleSaleFilter { SatisiVerildi = true }));
    }

    // ---------------------------------------------------------------- Bölüm B: BAF

    [Fact]
    public async Task Baf_yeni_alanlari_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BafService>();
        var onaylayan = Guid.NewGuid();
        var cikis = Saniye(Bugun.AddDays(-1));
        var donus = Saniye(Bugun);

        var id = await svc.CreateAsync(new BafInput
        {
            PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 10_000, Sube = "Merkez",
            KullanimAmaci = BafUsagePurpose.Yikama, Onaylayan = onaylayan, KirayaVer = true,
            CikisSaat = new TimeOnly(8, 30), CikisTarihi = cikis
        });

        var b = await svc.GetAsync(id);
        Assert.Equal(BafUsagePurpose.Yikama, b!.KullanimAmaci);
        Assert.Equal(onaylayan, b.Onaylayan);
        Assert.True(b.KirayaVer);
        Assert.Equal(new TimeOnly(8, 30), b.CikisSaat);
        Assert.Null(b.DonusSube);

        Assert.True(await svc.ReceiveAsync(id, returnKm: 10_500, returnFuel: 7, returnDate: donus,
            returnBranch: " Ankara ", returnHour: new TimeOnly(17, 45)));

        var b2 = await svc.GetAsync(id);
        Assert.Equal(BafStatus.Kapandi, b2!.Durum);
        Assert.Equal(10_500, b2.DonusKm);                     // çıkış 10.000 → dönüş 10.500 (elle)
        Assert.Equal("Ankara", b2.DonusSube);                 // trim
        Assert.Equal(new TimeOnly(17, 45), b2.DonusSaat);
        Assert.Equal("Merkez", b2.Sube);                      // çıkış şubesi DEĞİŞMEDİ (kapsam kaynağı)
        Assert.Equal(BafUsagePurpose.Yikama, b2.KullanimAmaci);
        Assert.Equal(new TimeOnly(8, 30), b2.CikisSaat);      // çıkış alanları teslimde kaymadı
    }

    [Fact]
    public async Task Baf_lokasyon_ve_amac_filtreleri()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BafService>();

        // ORACLE (elle): 1) A→A aynı ofis; 2) A→B farklı ofis; 3) A→(boş) hiçbir kovada değil.
        var ayni = await svc.CreateAsync(new BafInput
        { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 10, Sube = "A", KullanimAmaci = BafUsagePurpose.Yikama });
        await svc.ReceiveAsync(ayni, 20, null, returnBranch: "A");

        var farkli = await svc.CreateAsync(new BafInput
        { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 10, Sube = "A", KullanimAmaci = BafUsagePurpose.Muayene });
        await svc.ReceiveAsync(farkli, 20, null, returnBranch: "B");

        await svc.CreateAsync(new BafInput
        { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 10, Sube = "A" });   // açık, dönüşsüz

        Assert.Equal(3, (await svc.SearchAsync()).Count);

        var a = await svc.SearchAsync(new BafFilter { Lokasyon = BafLocation.AyniOfis });
        Assert.Single(a);
        Assert.Equal(ayni, a[0].Id);

        var f = await svc.SearchAsync(new BafFilter { Lokasyon = BafLocation.FarkliOfis });
        Assert.Single(f);
        Assert.Equal(farkli, f[0].Id);

        Assert.Single(await svc.SearchAsync(new BafFilter { KullanimAmaci = BafUsagePurpose.Muayene }));
        Assert.Empty(await svc.SearchAsync(new BafFilter { KullanimAmaci = BafUsagePurpose.YakitIkmali }));
        Assert.Single(await svc.SearchAsync(new BafFilter { Durum = BafStatus.Acik }));
        Assert.Equal(2, (await svc.SearchAsync(new BafFilter { Durum = BafStatus.Kapandi })).Count);
        Assert.Equal(3, (await svc.SearchAsync(new BafFilter { Ofis = "A" })).Count);
        Assert.Empty(await svc.SearchAsync(new BafFilter { Ofis = "C" }));
    }

    [Fact]
    public async Task Baf_filtresi_personel_plaka_ve_tarih()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BafService>();
        var arac1 = await AracAsync(scope, "06 BF 11");
        var arac2 = await AracAsync(scope, "06 BF 22");
        var pers = Guid.NewGuid();

        await svc.CreateAsync(new BafInput { PersonelId = pers, VehicleId = arac1, CikisKm = 1, CikisTarihi = Bugun });
        await svc.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = arac2, CikisKm = 1, CikisTarihi = Bugun.AddDays(-30) });

        Assert.Single(await svc.SearchAsync(new BafFilter { PersonelId = pers }));
        Assert.Single(await svc.SearchAsync(new BafFilter { Plaka = "06 BF 22" }));   // boşluklu yazım da bulmalı
        Assert.Single(await svc.SearchAsync(new BafFilter { Bas = Bugun.AddDays(-1) }));
        Assert.Equal(2, (await svc.SearchAsync(new BafFilter { Bit = Bugun })).Count);
    }

    [Fact]
    public async Task Baf_filtresi_sube_kapsamini_genisletemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))   // Admin
        {
            var svc = seed.ServiceProvider.GetRequiredService<BafService>();
            await svc.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 1, Sube = "Merkez" });
            await svc.CreateAsync(new BafInput { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 1, Sube = "Ankara" });
        }

        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var svc = op.ServiceProvider.GetRequiredService<BafService>();
            Assert.Single(await svc.SearchAsync());                                     // kapsam: yalnız Merkez
            Assert.Empty(await svc.SearchAsync(new BafFilter { Ofis = "Ankara" }));     // filtre kapsamı GENİŞLETMEZ
        }

        using (var admin = host.ScopeFor(tenant))
            Assert.Equal(2, (await admin.ServiceProvider.GetRequiredService<BafService>().SearchAsync()).Count);
    }

    [Fact]
    public async Task Baf_aramasi_yetki_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var muhasebe = host.ScopeFor(tenant, Guid.NewGuid(), "mh", UserRole.Muhasebe);
        // Muhasebe rolünde OperationsWrite YOK → BAF araması reddedilir (ekran da bu role kapalı).
        await Assert.ThrowsAsync<NoPermissionException>(
            () => muhasebe.ServiceProvider.GetRequiredService<BafService>().SearchAsync(new BafFilter()));
    }

    [Fact]
    public async Task Baf_arama_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<BafService>().CreateAsync(new BafInput
            { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 1, Sube = "Merkez", KullanimAmaci = BafUsagePurpose.Yikama });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<BafService>()
            .SearchAsync(new BafFilter { KullanimAmaci = BafUsagePurpose.Yikama }));
    }

    // ------------------------------------------------- Sayfa veri yolu (rol duman testi)

    /// <summary>
    /// OperatorSayfaErisimTests dersinin FAZ-18 karşılığı: liste sayfaları artık SearchAsync +
    /// kredi/poliçe/şube okumaları yapıyor. Sayfayı AÇABİLEN her rolde bu çağrıların HİÇBİRİ
    /// atmamalı — yoksa ekran 500 verir (bu hata sınıfı bu repoda iki kez yaşandı).
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Yonetici)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Muhasebe)]
    public async Task Satis_sayfasinin_veri_yolu_tum_rollerde_PATLAMAZ(UserRole rol)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant, Guid.NewGuid(), "u", rol);
        var sp = s.ServiceProvider;

        // VehicleSaleList.OnInitializedAsync'in yaptığı çağrıların AYNISI.
        Assert.NotNull(await sp.GetRequiredService<VehicleSaleService>().SearchAsync(new VehicleSaleFilter()));
        Assert.NotNull(await sp.GetRequiredService<VehicleService>().ListAsync());
        Assert.NotNull(await sp.GetRequiredService<RentACar.Application.Customers.CustomerService>().ListAsync());
        Assert.NotNull(await sp.GetRequiredService<RentACar.Application.Branches.BranchService>().ListActiveAsync());
        Assert.NotNull(await sp.GetRequiredService<RentACar.Application.AracKredileri.VehicleLoanService>().ListAsync());
        Assert.NotNull(await sp.GetRequiredService<RentACar.Application.Regulation.RegulationService>().ListInsuranceAsync());
    }

    /// <summary>BAF ekranı Admin/Yönetici/Operatör'e açık — SearchAsync o üç rolde de çalışmalı.</summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Yonetici)]
    [InlineData(UserRole.Operator)]
    public async Task Baf_sayfasinin_veri_yolu_acik_rollerde_PATLAMAZ(UserRole rol)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "u", rol, assignedBranch: "Merkez");
        var sp = s.ServiceProvider;
        Assert.NotNull(await sp.GetRequiredService<BafService>().SearchAsync(new BafFilter()));
        Assert.NotNull(await sp.GetRequiredService<RentACar.Application.Personnel.PersonnelService>().ListForSelectAsync());
        Assert.NotNull(await sp.GetRequiredService<RentACar.Application.Branches.BranchService>().ListActiveAsync());
    }

    /// <summary>
    /// Şube birleştirme kapsamı: <c>Baf.DonusSube</c> de bir METİN şube referansıdır. Birleştirmede
    /// taşınmazsa kaynak şube adı BAF dönüş alanında hayalet olarak kalırdı (SubeDerinlikTests'in
    /// kapsam guard'ı ad-bazlı olduğu için bunu yakalayamaz → burada ampirik kilit).
    /// </summary>
    [Fact]
    public async Task Sube_birlestirmede_BAF_donus_subesi_de_tasinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var subeler = sp.GetRequiredService<RentACar.Application.Branches.BranchService>();
        var eski = await subeler.CreateAsync(new RentACar.Application.Branches.BranchInput { Kod = "ESK", Ad = "Eski" });
        var yeni = await subeler.CreateAsync(new RentACar.Application.Branches.BranchInput { Kod = "YNI", Ad = "Yeni" });

        var baf = sp.GetRequiredService<BafService>();
        var id = await baf.CreateAsync(new BafInput
        { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 1, Sube = "Merkez" });
        await baf.ReceiveAsync(id, 2, null, returnBranch: "Eski");

        var onizleme = await subeler.PreviewMergeAsync(eski, yeni);
        Assert.Contains(onizleme!.Etkilenen, x => x.Tablo.Contains("dönüş", StringComparison.OrdinalIgnoreCase));

        await subeler.MergeAsync(eski, yeni);
        Assert.Equal("Yeni", (await baf.GetAsync(id))!.DonusSube);
        Assert.Equal("Merkez", (await baf.GetAsync(id))!.Sube);   // çıkış şubesi ilgisiz → değişmedi
    }
}
