using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.RateMatrices;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-79 — Karlılık/Gelir tablosu çok-boyutlu genişleme.
///
/// <para><b>BAĞIMSIZ ORACLE (elle kurulan senaryo, servis kodundan TÜRETİLMEZ):</b> tek araç,
/// filo girişi bugünden 9 gün önce → sahiplik 10 gün; 5 günü (bugün−9 … bugün−5) kirada → doluluk %50.
/// Deftere postlanan: gelir 10.000 (4 gün × 2.500 = 10.000 brüt, KDV'siz fatura) ve gider 4.000
/// (araç gideri, KDV 0) → Net 6.000. Araç KARTINDA (deftere GİRMEYEN) aylık maliyet 1.500 +
/// filo yönetim maliyeti 250 → referans toplam 1.750. Onaylı tarife matrisi Gün-7 = 800 →
/// potansiyel gelir 800 × 10 = 8.000.</para>
///
/// <para><b>Bu sınıfın asıl işi çift-sayımı YASAKLAMAK:</b> yukarıdaki referans sayıların HİÇBİRİ
/// Gelir/Gider/NetKar'a karışmamalı — Gider 4.000 kalmalı, 5.750 OLMAMALI.</para>
/// </summary>
[Collection("postgres")]
public sealed class KarlilikGenislemeTests(PostgresFixture fx)
{
    /// <summary>Bugünün UTC günü (saat 09:00) — CI/lokal tick farkına duyarsız, tam saniye hizalı.</summary>
    private static DateTimeOffset Gun(int gunOnce)
        => new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-gunOnce).AddHours(9);

    private sealed record Senaryo(Guid VehicleId, Guid CariId, Guid RentalId);

    /// <summary>Oracle senaryosunu kurar (yukarıdaki XML notundaki sayılar).</summary>
    private static async Task<Senaryo> KurAsync(IServiceProvider sp, string plaka = "34 KAR 79")
    {
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = plaka, Grup = "EKO", Sube = "Merkez", Segment = "Ekonomik", Sipp = "CDMR",
            AylikMaliyet = 1500m, FiloYonetimMaliyeti = 250m,
            AlimTarihi = Gun(9), FiloGirisTarih = Gun(9)
        });
        var cariId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Faz79", Soyad = "Musteri" });

        // Gider: araç gideri net 4.000 (KDV 0) → defter Borç Gider 4.000, AccountRef = araç.
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = vehicleId, NetTutar = 4000m, KdvOrani = 0m,
            Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Aciklama = "Bakım"
        });

        // Gelir: 4 gün × 2.500 = 10.000 brüt; KDV'siz fatura → defter Alacak Gelir 10.000.
        var rentalId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cariId, VehicleId = vehicleId, BasTar = Gun(9), BitTar = Gun(5),
            GunlukUcret = 2500m, Kaynak = "WEB"
        });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId, kdvRate: 0m);
        return new Senaryo(vehicleId, cariId, rentalId);
    }

    /// <summary>Onaylı + aktif tarife matrisi (grup EKO, Gün-7 = 800).</summary>
    private static Task<Guid> TarifeKurAsync(IServiceProvider sp, decimal gun7 = 800m, string kod = "F79")
        => sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = kod, Ad = "FAZ-79 tarife", AracGrupKod = "EKO",
            Gun1 = gun7, Gun2 = gun7, Gun3 = gun7, Gun4 = gun7, Gun5 = gun7, Gun6 = gun7, Gun7 = gun7,
            OnayDurumu = TarifeOnayDurumu.Onayli, Aktif = true
        });

    // ---------------------------------------------------------------------------------------------
    // 1) ÇİFT-SAYIM YASAĞI — bu fazın kilit testi
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Referans_maliyet_satirda_gorunur_ama_PnL_toplamina_KARISMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var s = await KurAsync(sp);

        var k = await sp.GetRequiredService<ReportService>().GetKarlilikAsync();
        var row = Assert.Single(k.Satirlar);

        // P&L — DEFTERDEN (elle oracle).
        Assert.Equal(s.VehicleId, row.VehicleId);
        Assert.Equal(10000m, row.Gelir);
        Assert.Equal(4000m, row.Gider);   // 5.500 ya da 5.750 OLURSA referans maliyet sızmış demektir
        Assert.Equal(6000m, row.NetKar);
        Assert.Equal(10000m, k.ToplamGelir);
        Assert.Equal(4000m, k.ToplamGider);
        Assert.Equal(6000m, k.ToplamNetKar);

        // Referans — AYRI kolonlarda GÖRÜNÜR.
        Assert.Equal(1500m, row.ReferansAylikMaliyet);
        Assert.Equal(250m, row.ReferansFiloYonetimMaliyeti);
        Assert.Equal(1750m, row.ReferansToplamMaliyet);
        Assert.Equal(1750m, k.ToplamReferansMaliyet);
    }

    [Fact]
    public async Task Referans_maliyet_ucuk_degere_cekilince_PnL_BIT_BIREBIR_AYNI_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var s = await KurAsync(sp);
        var rs = sp.GetRequiredService<ReportService>();

        var once = await rs.GetKarlilikAsync();

        // Araç kartındaki referans alanları UÇUK değerlere çekilir + tarife eklenir (potansiyel gelir doğar).
        await sp.GetRequiredService<VehicleService>().UpdateAsync(s.VehicleId, new VehicleInput
        {
            Plaka = "34 KAR 79", Grup = "EKO", Sube = "Merkez", Segment = "Ekonomik", Sipp = "CDMR",
            AylikMaliyet = 999_999m, FiloYonetimMaliyeti = 888_888m,
            AlimTarihi = Gun(9), FiloGirisTarih = Gun(9)
        });
        await TarifeKurAsync(sp);

        var sonra = await rs.GetKarlilikAsync();

        // KIRILGAN REGRESYON: para kolonları BİT-BİREBİR aynı.
        Assert.Equal(once.ToplamGelir, sonra.ToplamGelir);
        Assert.Equal(once.ToplamGider, sonra.ToplamGider);
        Assert.Equal(once.ToplamNetKar, sonra.ToplamNetKar);
        Assert.Equal(once.Satirlar.Count, sonra.Satirlar.Count);
        for (var i = 0; i < once.Satirlar.Count; i++)
        {
            Assert.Equal(once.Satirlar[i].Gelir, sonra.Satirlar[i].Gelir);
            Assert.Equal(once.Satirlar[i].Gider, sonra.Satirlar[i].Gider);
            Assert.Equal(once.Satirlar[i].NetKar, sonra.Satirlar[i].NetKar);
        }
        // …ama referans kolonları GERÇEKTEN değişti (test boş yere yeşil değil).
        Assert.Equal(999_999m, sonra.Satirlar[0].ReferansAylikMaliyet);
        Assert.Equal(1_888_887m, sonra.Satirlar[0].ReferansToplamMaliyet);
        Assert.NotNull(sonra.Satirlar[0].PotansiyelGelir);
    }

    // ---------------------------------------------------------------------------------------------
    // 2) Σ ÇAPRAZ DOĞRULAMA — rapor kendi kendini değil, AYRI bir sorgu doğrular
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Satir_toplamlari_defterden_BAGIMSIZ_sorguyla_esit()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await KurAsync(sp);
        // İkinci araç + atfedilemeyen genel gider (Atanmamış satırı da toplamda olmalı).
        await KurAsync(sp, "34 KAR 80");
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = 777m, KdvOrani = 0m,
            Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit
        });

        var k = await sp.GetRequiredService<ReportService>().GetKarlilikAsync();

        // BAĞIMSIZ sorgu: ReportService/ReportRepository ÇAĞRILMADAN, doğrudan defterden.
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var defter = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType != "DonemKapanis")
            .Select(e => new { e.AccountType, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync();
        var defterGelir = defter.Where(e => e.AccountType == LedgerAccountType.Gelir)
            .Sum(e => (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R);
        var defterGider = defter.Where(e => e.AccountType == LedgerAccountType.Gider)
            .Sum(e => (e.Direction == LedgerDirection.Debit ? 1m : -1m) * e.A * e.R);

        Assert.Equal(defterGelir, k.ToplamGelir);
        Assert.Equal(defterGider, k.ToplamGider);
        Assert.Equal(20000m, defterGelir);          // 2 × 10.000 (elle)
        Assert.Equal(8777m, defterGider);           // 2 × 4.000 + 777 (elle)
        Assert.Contains(k.Satirlar, r => r.VehicleId == null && r.Gider == 777m);
    }

    // ---------------------------------------------------------------------------------------------
    // 3) YENİ KOLONLAR
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Yeni_kolonlar_dogru_kaynaktan_gelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var s = await KurAsync(sp);
        await TarifeKurAsync(sp);

        var row = Assert.Single((await sp.GetRequiredService<ReportService>().GetKarlilikAsync()).Satirlar);

        Assert.Equal("CDMR", row.Sipp);                 // araç kartı
        Assert.Equal("Merkez", row.Otopark);            // şube FK (yoksa metin)
        Assert.Equal("WEB", row.RezKaynagi);            // kiranın kaynağı
        Assert.Equal("Faz79 Musteri", row.CariAd);      // son kiranın müşterisi
        Assert.Equal(1, row.KiraAdet);

        // Sahiplik penceresi 10 gün (bugün−9 … bugün) — kirada 5 gün (bugün−9 … bugün−5, kapsayıcı).
        Assert.Equal(10, row.SahiplikGun);
        Assert.Equal(5, row.KiralananGun);
        Assert.Equal(50.00m, row.DolulukYuzde);
        Assert.Equal(1000.00m, row.RevPacd);            // 10.000 ÷ 10 gün (elle)
        Assert.Equal(2000.00m, row.Adr);                // 10.000 ÷ 5 gün (elle)

        // Potansiyel gelir: onaylı tarife Gün-7 = 800 × 10 sahiplik günü (elle).
        Assert.Equal(8000.00m, row.PotansiyelGelir);
        Assert.Equal(8000.00m, (await sp.GetRequiredService<ReportService>().GetKarlilikAsync()).ToplamPotansiyelGelir);

        // Cari bakiye: 10.000 fatura borcu, tahsilat yok → cari 10.000 borçlu (CARİ defterinden).
        Assert.Equal(10000m, row.CariBakiye);
        Assert.Equal(s.VehicleId, row.VehicleId);
    }

    [Fact]
    public async Task Tarife_yoksa_potansiyel_gelir_NULL_dondurur_sifir_DEGIL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await KurAsync(sp);

        var row = Assert.Single((await sp.GetRequiredService<ReportService>().GetKarlilikAsync()).Satirlar);
        Assert.Null(row.PotansiyelGelir);   // "Hesaplanmadı" — 0 yanıltıcı olurdu
        Assert.Null((await sp.GetRequiredService<ReportService>().GetKarlilikAsync()).ToplamPotansiyelGelir);
    }

    [Fact]
    public async Task Onaysiz_tarife_potansiyel_gelire_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await KurAsync(sp);
        // Bekleyen (onaysız) tarife — fiyat motoru da bunu seçmez; rapor da seçmemeli.
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "F79BEK", Ad = "Onaysız", AracGrupKod = "EKO", Gun7 = 5000m,
            OnayDurumu = TarifeOnayDurumu.Bekliyor, Aktif = true
        });

        var row = Assert.Single((await sp.GetRequiredService<ReportService>().GetKarlilikAsync()).Satirlar);
        Assert.Null(row.PotansiyelGelir);
    }

    // ---------------------------------------------------------------------------------------------
    // 4) KDV DURUM — salt gösterim
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task KdvDurum_toggle_Gelir_Gider_NetKar_DEGERLERINI_degistirmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 KDV 79", Grup = "EKO", Sube = "Merkez", AlimTarihi = Gun(9), FiloGirisTarih = Gun(9) });
        var cariId = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kdv", Soyad = "Cari" });
        // 4 gün × 300 = 1.200 brüt, %20 KDV → net 1.000 + KDV 200 (elle).
        var rentalId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cariId, VehicleId = vehicleId, BasTar = Gun(9), BitTar = Gun(5), GunlukUcret = 300m });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId, kdvRate: 0.20m);

        var rs = sp.GetRequiredService<ReportService>();
        var kdvsiz = await rs.GetKarlilikAsync(kdvDurum: KdvDurum.Kdvsiz);
        var dahil = await rs.GetKarlilikAsync(kdvDurum: KdvDurum.KdvDahil);

        Assert.Equal(1000m, kdvsiz.Satirlar[0].Gelir);              // defter NET (elle)
        Assert.Equal(kdvsiz.Satirlar[0].Gelir, dahil.Satirlar[0].Gelir);
        Assert.Equal(kdvsiz.Satirlar[0].Gider, dahil.Satirlar[0].Gider);
        Assert.Equal(kdvsiz.Satirlar[0].NetKar, dahil.Satirlar[0].NetKar);
        Assert.Equal(kdvsiz.ToplamNetKar, dahil.ToplamNetKar);

        // KDV REFERANS kolonu her iki modda da aynı veriyi taşır (mod yalnız GÖSTERİMİ değiştirir).
        Assert.Equal(200m, dahil.Satirlar[0].HesaplananKdv);
        Assert.Equal(1200m, dahil.Satirlar[0].GelirKdvDahil);       // 1.000 + 200 (elle) — Gelir DEĞİL
        Assert.Equal(KdvDurum.KdvDahil, dahil.KdvDurum);
    }

    [Fact]
    public async Task Gider_KDVsi_arac_KDV_kolonuna_KARISMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "34 KDV 80", Grup = "EKO", Sube = "Merkez", AlimTarihi = Gun(9), FiloGirisTarih = Gun(9) });
        // Yalnız GİDER (indirilecek KDV 200) — satış belgesi yok.
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = vehicleId, NetTutar = 1000m, KdvOrani = 0.20m,
            Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit
        });

        var row = Assert.Single((await sp.GetRequiredService<ReportService>()
            .GetKarlilikAsync(kdvDurum: KdvDurum.KdvDahil)).Satirlar);
        Assert.Equal(1000m, row.Gider);
        Assert.Null(row.HesaplananKdv);     // alış KDV'si satış KDV kolonuna GİRMEZ
        Assert.Null(row.GelirKdvDahil);
    }

    // ---------------------------------------------------------------------------------------------
    // 5) FİLTRE + BOYUT ÖZETİ
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Kaynak_ve_sipp_filtresi_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await KurAsync(sp);                       // Kaynak = WEB, SIPP = CDMR

        var rs = sp.GetRequiredService<ReportService>();
        Assert.Single((await rs.GetKarlilikAsync(kaynak: "WEB")).Satirlar);
        Assert.Empty((await rs.GetKarlilikAsync(kaynak: "ACENTA")).Satirlar);
        Assert.Single((await rs.GetKarlilikAsync(sipp: "cdmr")).Satirlar);   // harf duyarsız
        Assert.Empty((await rs.GetKarlilikAsync(sipp: "IDAR")).Satirlar);
    }

    [Fact]
    public async Task Otopark_ve_sipp_boyut_ozetleri_araci_TEK_kovaya_koyar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await KurAsync(sp);
        await KurAsync(sp, "34 KAR 81");

        var rs = sp.GetRequiredService<ReportService>();
        var otopark = await rs.GetKarlilikOzetAsync("otopark");
        var satir = Assert.Single(otopark.Satirlar);
        Assert.Equal("Merkez", satir.Boyut);
        Assert.Equal(2, satir.AracAdet);
        Assert.Equal(20000m, satir.Gelir);                 // 2 × 10.000 (elle)
        Assert.Equal(8000m, satir.Gider);                  // 2 × 4.000 (elle)
        Assert.Equal(10000m, satir.AracBasiGelir);         // 20.000 ÷ 2 araç (elle)
        Assert.Equal(50.00m, satir.DolulukYuzde);          // havuz: 10 kiralanan ÷ 20 sahiplik
        Assert.Equal(3500m, satir.ReferansToplamMaliyet);  // 2 × 1.750 — P&L'e girmez
        Assert.Equal(20000m, otopark.ToplamGelir);
        Assert.Equal(8000m, otopark.ToplamGider);

        var sipp = await rs.GetKarlilikOzetAsync("sipp");
        Assert.Equal("CDMR", Assert.Single(sipp.Satirlar).Boyut);
        // Boyut değişse de P&L toplamı SABİT.
        Assert.Equal(otopark.ToplamNetKar, sipp.ToplamNetKar);
    }

    // ---------------------------------------------------------------------------------------------
    // 6) TENANT İZOLASYONU + YETKİ
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tenant_izolasyonu_referans_kolonlara_da_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);   // racar_app (RLS zorunlu)
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var a = host.ScopeFor(tenantA))
        {
            await KurAsync(a.ServiceProvider);
            await TarifeKurAsync(a.ServiceProvider);
        }
        using var b = host.ScopeFor(tenantB);
        var k = await b.ServiceProvider.GetRequiredService<ReportService>().GetKarlilikAsync();

        Assert.Empty(k.Satirlar);
        Assert.Equal(0m, k.ToplamGelir);
        Assert.Null(k.ToplamPotansiyelGelir);       // A'nın tarifesi B'ye sızmaz
        Assert.Null(k.ToplamReferansMaliyet);
    }

    [Fact]
    public async Task Operator_rolu_icin_yeni_yetki_kapisi_ACILMADI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
        {
            await KurAsync(admin.ServiceProvider);
            await TarifeKurAsync(admin.ServiceProvider);
        }

        // Rapor servisi yetki guard'ı taşımaz (yüzey Web/[Authorize] + export ViewReports'tadır);
        // FAZ-79 zenginleştirmesi PII/ManageUsers gerektiren bir yola SAPMAMALI — operatörde patlamamalı.
        using var op = host.ScopeFor(tenant, role: UserRole.Operator);
        var k = await op.ServiceProvider.GetRequiredService<ReportService>().GetKarlilikAsync();
        var row = Assert.Single(k.Satirlar);
        Assert.Equal(6000m, row.NetKar);
        Assert.Equal(8000.00m, row.PotansiyelGelir);
    }
}
