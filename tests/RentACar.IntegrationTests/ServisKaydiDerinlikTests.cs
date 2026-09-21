using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-16 — servis kaydı derinliği: kaza/fatura/ödeme blokları + yakıt + kalem bileşenleri +
/// "Rezerve" randevu durumu.
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler ELLE kurulan senaryodan gelir — 250 × 3 − 50 = 700;
/// 1.000 + 180 = 1.180; 33,33 × %20 = 6,67 (satır) → 3 satırda 20,01; rücu 1.000 × 0,5 = 500.
/// Hiçbir beklenen değer servis/rapor kodundan türetilmedi.</para>
///
/// <para><b>KİLİTLİ KARAR (docs/KARARLAR.md "FAZ-16"): fatura/ödeme DEFTERE BAĞLANMAZ.</b>
/// <c>Fatura_ve_odeme_bilgisi_*</c> testleri kasıtlı olarak KIRILGANDIR: bu alanlardan herhangi bir
/// <c>AccountLedgerEntry</c> ya da gider doğarsa defter satır sayısı / gelir-gider toplamı / araç
/// karnesi rakamları değişir ve test patlar. Çift-sayıma karşı kalıcı kilittir.</para>
/// </summary>
[Collection("postgres")]
public sealed class ServisKaydiDerinlikTests(PostgresFixture fx)
{
    // Gün-hassas rapor testleri için whole-second hizalı taban (CI'da µs/100ns tick farkı tuzağı).
    private static readonly DateTimeOffset Taban =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-10).AddHours(9);
    private static readonly DateTimeOffset Gecmis = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static Task<Guid> AracAsync(IServiceProvider sp, string plaka)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });

    private static async Task<VehicleStatus> AracDurumAsync(IServiceProvider sp, Guid id)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return (await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == id)).Durum;
    }

    private static async Task<int> DefterSatirSayisiAsync(IServiceProvider sp)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await db.AccountLedgerEntries.CountAsync();
    }

    /// <summary>ELLE doldurulmuş bilgi bloğu — round-trip oracle'ı (değerler sabit).</summary>
    private static void BilgiDoldur(ServiceRecordBilgiInput b)
    {
        b.AtolyeAdi = "Merkez Atölye";
        b.Aciklama = "Sağ ön çamurluk";

        b.BeyanTuru = "Kaza Tespit Tutanağı";
        b.KarsiPlaka = "06 xy 999";          // normalize edilerek büyük harfe çekilir
        b.KarsiTrafikSigortasi = "Anadolu Sigorta / TRF-778";
        b.KazaTarihi = Gecmis;
        b.KazaSorumlusu = "Karşı sürücü";
        b.HasarDosyaNo = "HD-2026-4451";
        b.DegerKaybi = 7_500.50m;

        b.FaturaTarihi = Gecmis.AddDays(3);
        b.FaturaNo = "SRV-FTR-9001";
        b.FaturaTutar = 1_000m;
        b.FaturaKdv = 180m;                  // → GenelToplam 1.180 (ELLE)

        b.OdemeTarihi = Gecmis.AddDays(5);
        b.Odeme = 1_180m;
        b.OdemeDoviz = "eur";                // normalize → EUR
        b.OdemeKur = 38.4512m;
        b.OdemeTuru = OdemeYontemi.Banka;
        b.KasaKodu = "KASA-01";
        b.HesapNo = "TR33 0006 1005 1978 6457 8413 26";

        b.CikisYakit = 4;
        b.DonusYakit = 9;
        b.PlanBasTarihi = Gecmis.AddDays(-2);
        b.PlanBitTarihi = Gecmis.AddDays(-1);
    }

    // ==================== A — alan turu (round-trip) ====================

    [Fact]
    public async Task Yeni_alanlar_kayitta_ve_guncellemede_birebir_round_trip_eder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 01");

        var girdi = new ServiceRecordInput { VehicleId = arac, Tip = ServisTipi.Ariza, GirisKm = 51_000 };
        BilgiDoldur(girdi);
        var id = await svc.CreateAsync(girdi);

        var rec = await svc.GetAsync(id);
        Assert.NotNull(rec);
        Assert.Equal("Merkez Atölye", rec!.AtolyeAdi);
        Assert.Equal("Kaza Tespit Tutanağı", rec.BeyanTuru);
        Assert.Equal("06 XY 999", rec.KarsiPlaka);                 // ELLE: büyük harfe normalize
        Assert.Equal("Anadolu Sigorta / TRF-778", rec.KarsiTrafikSigortasi);
        Assert.Equal(Gecmis, rec.KazaTarihi);
        Assert.Equal("Karşı sürücü", rec.KazaSorumlusu);
        Assert.Equal("HD-2026-4451", rec.HasarDosyaNo);
        Assert.Equal(7_500.50m, rec.DegerKaybi);
        Assert.Equal(Gecmis.AddDays(3), rec.FaturaTarihi);
        Assert.Equal("SRV-FTR-9001", rec.FaturaNo);
        Assert.Equal(1_000m, rec.FaturaTutar);
        Assert.Equal(180m, rec.FaturaKdv);
        Assert.Equal(1_180m, rec.FaturaGenelToplam);               // ELLE: 1.000 + 180
        Assert.Equal(Gecmis.AddDays(5), rec.OdemeTarihi);
        Assert.Equal(1_180m, rec.Odeme);
        Assert.Equal("EUR", rec.OdemeDoviz);                       // ELLE: normalize
        Assert.Equal(38.4512m, rec.OdemeKur);
        Assert.Equal(OdemeYontemi.Banka, rec.OdemeTuru);
        Assert.Equal("KASA-01", rec.KasaKodu);
        Assert.Equal("TR33 0006 1005 1978 6457 8413 26", rec.HesapNo);
        Assert.Equal(4, rec.CikisYakit);
        Assert.Equal(9, rec.DonusYakit);
        Assert.Equal(Gecmis.AddDays(-2), rec.PlanBasTarihi);
        Assert.Equal(Gecmis.AddDays(-1), rec.PlanBitTarihi);

        // Güncelleme yolu (fatura genelde servis bittikten SONRA gelir).
        var guncel = new ServiceRecordBilgiInput { FaturaNo = "SRV-FTR-9002", FaturaTutar = 2_000m, FaturaKdv = 400m };
        Assert.True(await svc.BilgiGuncelleAsync(id, guncel));
        var rec2 = await svc.GetAsync(id);
        Assert.Equal("SRV-FTR-9002", rec2!.FaturaNo);
        Assert.Equal(2_400m, rec2.FaturaGenelToplam);              // ELLE: 2.000 + 400
        // Verilmeyen alanlar temizlenir (form TÜM bloğu gönderir — kısmi yama değil, tam yazma).
        Assert.Null(rec2.KasaKodu);
    }

    [Fact]
    public async Task Bilgi_guncellemesi_durum_km_ve_iscilik_toplamina_DOKUNMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 02");

        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = arac, GirisKm = 12_345,
            Lines = [new ServiceLineInput { Aciklama = "Yağ", Tutar = 800m }]
        });
        await svc.BaslatAsync(id);

        var girdi = new ServiceRecordBilgiInput();
        BilgiDoldur(girdi);
        Assert.True(await svc.BilgiGuncelleAsync(id, girdi));

        var rec = await svc.GetAsync(id);
        // Whitelist TİP düzeyinde: bu alanlar ServiceRecordBilgiInput'ta yok → değişemezler.
        Assert.Equal(ServisDurum.Serviste, rec!.Durum);
        Assert.Equal(12_345, rec.GirisKm);
        Assert.Equal(800m, rec.ToplamIscilik);
        Assert.False(rec.Yansitildi);
        Assert.Equal(0m, rec.YansitilanTutar);
    }

    // ==================== B — kalem bileşenleri + satır bazlı yuvarlama ====================

    [Fact]
    public async Task Kalem_tutari_bilesenlerden_TURETILIR_acik_tutar_KAZANIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 03");

        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = arac, GirisKm = 0,
            Lines =
            [
                // ELLE: 250 × 3 = 750; − 50 indirim = 700 (KDV net'e KARIŞMAZ).
                new ServiceLineInput { Aciklama = "Kaporta", BirimFiyat = 250m, Miktar = 3m, Indirim = 50m, KdvOran = 0.20m },
                // Açık tutar verilirse bileşenler yalnız BİLGİDİR: 999 kazanır (120 × 2 = 240 DEĞİL).
                new ServiceLineInput { Aciklama = "Boya", BirimFiyat = 120m, Miktar = 2m, Tutar = 999m }
            ]
        });

        var rec = await svc.GetAsync(id);
        var kaporta = rec!.Lines.Single(l => l.Aciklama == "Kaporta");
        var boya = rec.Lines.Single(l => l.Aciklama == "Boya");
        Assert.Equal(700m, kaporta.Tutar);      // ELLE
        Assert.Equal(250m, kaporta.BirimFiyat);
        Assert.Equal(3m, kaporta.Miktar);
        Assert.Equal(50m, kaporta.Indirim);
        Assert.Equal(0.20m, kaporta.KdvOran);
        Assert.Equal(999m, boya.Tutar);         // ELLE: açık tutar kazandı
        Assert.Equal(1_699m, rec.ToplamIscilik); // ELLE: 700 + 999

        // Miktarsız birim fiyat → 1 kabul edilir ve KALICI yazılır.
        Assert.True(await svc.KalemEkleAsync(id, new ServiceLineInput { Aciklama = "Trim", BirimFiyat = 300m }));
        var trim = (await svc.GetAsync(id))!.Lines.Single(l => l.Aciklama == "Trim");
        Assert.Equal(300m, trim.Tutar);          // ELLE: 300 × 1
        Assert.Equal(1m, trim.Miktar);
        Assert.Equal(1_999m, (await svc.GetAsync(id))!.ToplamIscilik); // ELLE: 1.699 + 300
    }

    /// <summary>
    /// YUVARLAMA ARTIĞI NEREYE GİDİYOR — açıkça yazılı: satır bazında yuvarlanır, artık SATIRDA kalır.
    /// ELLE: 3 satır × 33,33 net, %20 KDV. Satır KDV'si 33,33 × 0,20 = 6,666 → 6,67 (AwayFromZero).
    /// Belge KDV'si 3 × 6,67 = 20,01. Toplamdan hesaplansaydı 99,99 × 0,20 = 19,998 → 20,00 olurdu;
    /// aradaki 0,01 bilinçli olarak SATIRLARDA durur (kullanıcı kalemi kalem doğruluyor).
    /// </summary>
    [Fact]
    public void Satir_bazli_KDV_yuvarlamasi_artigi_SATIRDA_kalir()
    {
        var satirlar = new[] { 33.33m, 33.33m, 33.33m };

        var satirKdvleri = satirlar.Select(n => ServisKalemHesap.Hesapla(n, null, null, 0.20m).KdvTutar).ToArray();
        Assert.All(satirKdvleri, k => Assert.Equal(6.67m, k));       // ELLE: 6,666 → 6,67

        var net = satirlar.Sum();
        var kdvSatirBazli = satirKdvleri.Sum();
        Assert.Equal(99.99m, net);                                    // ELLE
        Assert.Equal(20.01m, kdvSatirBazli);                          // ELLE: 3 × 6,67
        Assert.Equal(120.00m, net + kdvSatirBazli);                   // ELLE

        // Toplam-bazlı (KULLANILMAYAN) yol farklı çıkar; fark tam 0,01'dir ve satırlarda kalır.
        var kdvToplamBazli = decimal.Round(net * 0.20m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(20.00m, kdvToplamBazli);
        Assert.Equal(0.01m, kdvSatirBazli - kdvToplamBazli);

        // Brüt de satır bazında yuvarlanır: 12,345 × 3 = 37,035 → 37,04 (indirimsiz net).
        Assert.Equal(37.04m, ServisKalemHesap.Net(12.345m, 3m));
    }

    // ==================== C — Rezerve randevu akışı ====================

    [Fact]
    public async Task Rezerve_kayit_Servise_Al_ile_acilir_arac_durumu_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 04");

        // TARİH TABANI SANİYE HİZALI: PostgreSQL timestamptz MİKROSANİYE çözünürlüklüdür, .NET
        // DateTimeOffset 100ns'lik tick kullanır. Ham UtcNow yazılıp geri okunduğunda 3 tick'lik
        // (300 ns) fark kalıyor ve eşitlik CI'da (Linux saat çözünürlüğü) patlıyordu — Mac'te
        // tesadüfen hizalı olduğu için lokalde yeşildi.
        var plan = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3), DateTimeKind.Utc), TimeSpan.Zero);
        plan = plan.AddTicks(-(plan.Ticks % TimeSpan.TicksPerSecond));
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = arac, Tip = ServisTipi.Periyodik, GirisKm = 0, Rezervasyon = true,
            PlanBasTarihi = plan, PlanBitTarihi = plan.AddDays(1)
        });

        var rez = await svc.GetAsync(id);
        Assert.Equal(ServisDurum.Rezerve, rez!.Durum);
        Assert.Equal(plan, rez.PlanBasTarihi);
        // Randevu aracı bakıma SOKMAZ.
        Assert.Equal(VehicleStatus.Musait, await AracDurumAsync(sp, arac));

        var oncesi = DateTimeOffset.UtcNow.AddSeconds(-2);
        Assert.True(await svc.ServiseAlAsync(id, girisKm: 62_500));
        var acik = await svc.GetAsync(id);
        Assert.Equal(ServisDurum.Acik, acik!.Durum);                  // ELLE: sabit hedef durum
        Assert.Equal(62_500, acik.GirisKm);                           // ELLE
        Assert.InRange(acik.GirisTarihi, oncesi, DateTimeOffset.UtcNow.AddSeconds(2)); // "o an"
        Assert.Equal(plan, acik.PlanBasTarihi);                       // plan KORUNUR (plan-gerçek farkı ölçülebilsin)
        Assert.Equal(VehicleStatus.Musait, await AracDurumAsync(sp, arac));

        // Buradan sonrası mevcut akışın AYNISI: Açık → Serviste → Tamamlandı.
        Assert.True(await svc.BaslatAsync(id));
        Assert.Equal(VehicleStatus.Serviste, await AracDurumAsync(sp, arac));
        Assert.True(await svc.TamamlaAsync(id, cikisKm: 62_600));
        Assert.Equal(ServisDurum.Tamamlandi, (await svc.GetAsync(id))!.Durum);
        Assert.Equal(VehicleStatus.Musait, await AracDurumAsync(sp, arac));
    }

    [Fact]
    public async Task Rezerve_gecersiz_gecisleri_reddeder_iptal_edilebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 05");

        var rezId = await svc.CreateAsync(new ServiceRecordInput { VehicleId = arac, Rezervasyon = true });
        // Randevu gerçekleşmeden araç servise giremez / kayıt tamamlanamaz.
        await Assert.ThrowsAsync<ValidationException>(() => svc.BaslatAsync(rezId));
        await Assert.ThrowsAsync<ValidationException>(() => svc.TamamlaAsync(rezId, 100));

        // Normal (Açık) kayıt "Servise Al" edilemez — o yalnız randevu açma aksiyonudur.
        var acikId = await svc.CreateAsync(new ServiceRecordInput { VehicleId = arac });
        await Assert.ThrowsAsync<ValidationException>(() => svc.ServiseAlAsync(acikId));

        // Randevu iptal edilebilir; araç zaten Serviste olmadığı için durumu değişmez.
        Assert.True(await svc.IptalAsync(rezId));
        Assert.Equal(ServisDurum.Iptal, (await svc.GetAsync(rezId))!.Durum);
        Assert.Equal(VehicleStatus.Musait, await AracDurumAsync(sp, arac));
        // Kapanmış kayıt ikinci kez servise alınamaz.
        await Assert.ThrowsAsync<ValidationException>(() => svc.ServiseAlAsync(rezId));
    }

    [Fact]
    public async Task Rezerve_ve_Iptal_BAKIM_gunu_olarak_SAYILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var rezerveli = await AracAsync(sp, "34 RZ 01");
        var iptalli = await AracAsync(sp, "34 RZ 02");
        var gercek = await AracAsync(sp, "34 RZ 03");

        // ELLE KURULAN TAKVİM — aralık T+0 … T+2 (3 gün). Üç aracın da servis kaydı AYNI aralığı
        // kapsıyor; yalnız GERÇEKLEŞMİŞ (Tamamlandı) olan bakım günü saymalı.
        await using (var db = await f.CreateDbContextAsync())
        {
            db.ServiceRecords.Add(new RentACar.Domain.Entities.ServiceRecord
            {
                No = "SRV-RZ1", VehicleId = rezerveli, Durum = ServisDurum.Rezerve,
                GirisTarihi = Taban, CikisTarihi = Taban.AddDays(2),
                PlanBasTarihi = Taban, PlanBitTarihi = Taban.AddDays(2)
            });
            db.ServiceRecords.Add(new RentACar.Domain.Entities.ServiceRecord
            {
                No = "SRV-RZ2", VehicleId = iptalli, Durum = ServisDurum.Iptal,
                GirisTarihi = Taban, CikisTarihi = Taban.AddDays(2)
            });
            db.ServiceRecords.Add(new RentACar.Domain.Entities.ServiceRecord
            {
                No = "SRV-RZ3", VehicleId = gercek, Durum = ServisDurum.Tamamlandi,
                GirisTarihi = Taban, CikisTarihi = Taban.AddDays(2)
            });
            await db.SaveChangesAsync();
        }

        var rs = sp.GetRequiredService<ReportService>();
        var satirlar = await rs.GetAracDurumTakipAracBazliAsync(null, Taban, Taban.AddDays(2));

        var rz1 = satirlar.Single(r => r.Plaka == "34RZ01");
        Assert.Equal(0, rz1.BakimGun);   // ELLE: randevu gerçekleşmedi
        Assert.Equal(3, rz1.BosGun);     // ELLE: T+0..T+2

        var rz2 = satirlar.Single(r => r.Plaka == "34RZ02");
        Assert.Equal(0, rz2.BakimGun);   // FAZ-76 davranışı KORUNUYOR
        Assert.Equal(3, rz2.BosGun);

        var rz3 = satirlar.Single(r => r.Plaka == "34RZ03");
        Assert.Equal(3, rz3.BakimGun);   // ELLE: kontrol — gerçek servis sayılır
        Assert.Equal(0, rz3.BosGun);

        // Gün kırılımı (filo geneli) da aynı kuralı uygular: 3 araçtan yalnız 1'i bakımda.
        var gunler = await rs.GetAracDurumTakipAsync(Taban, Taban.AddDays(2));
        Assert.All(gunler, g => Assert.Equal(1, g.Bakim));
    }

    // ==================== D — KIRILGAN REGRESYON: defter/gider ETKİSİ YOK ====================

    [Fact]
    public async Task Fatura_ve_odeme_bilgisi_deftere_ve_raporlara_YANSIMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var rs = sp.GetRequiredService<ReportService>();
        var arac = await AracAsync(sp, "34 SD 06");

        // GERÇEK maliyet: Giderler ekranından (tek doğru yol) — net 500, KDV yok.
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = arac, NetTutar = 500m, KdvOrani = 0m,
            Tarih = Taban, OdemeYontemi = OdemeYontemi.Nakit
        });

        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = arac, GirisKm = 1_000,
            Lines = [new ServiceLineInput { Aciklama = "Onarım", Tutar = 400m }]
        });
        await svc.BaslatAsync(id);
        await svc.TamamlaAsync(id, cikisKm: 1_100);

        // ---- ÖNCE: durum fotoğrafı
        var defterOnce = await DefterSatirSayisiAsync(sp);
        var ggOnce = await rs.GetGelirGiderAsync(null, null);
        var karneOnce = await rs.GetAracKarneAsync(arac);
        var servisMaliyetOnce = await rs.GetServiceCostSummaryAsync(null, null);
        Assert.Equal(500m, ggOnce.GiderToplam);            // ELLE: yalnız Gider kaydı
        Assert.Equal(500m, karneOnce!.ToplamGider);        // ELLE: servis kaydı deftere GİRMEZ
        Assert.Equal(400m, servisMaliyetOnce.Single().Toplam); // operasyonel maliyet raporu (defter değil)

        // ---- UÇUK fatura + ödeme bilgisi yazılır
        Assert.True(await svc.BilgiGuncelleAsync(id, new ServiceRecordBilgiInput
        {
            FaturaNo = "ABARTI-1", FaturaTutar = 999_999_999m, FaturaKdv = 179_999_999.82m,
            Odeme = 1_234_567_890m, OdemeDoviz = "USD", OdemeKur = 41.5m,
            OdemeTuru = OdemeYontemi.Banka, KasaKodu = "KASA-99", DegerKaybi = 88_888m,
            FaturaTarihi = Taban, OdemeTarihi = Taban
        }));

        // Yazıldığını doğrula (test boşa geçmesin).
        var rec = await svc.GetAsync(id);
        Assert.Equal(999_999_999m, rec!.FaturaTutar);
        Assert.Equal(1_179_999_998.82m, rec.FaturaGenelToplam);  // ELLE: 999.999.999 + 179.999.999,82

        // ---- SONRA: hiçbir mali rakam DEĞİŞMEDİ
        Assert.Equal(defterOnce, await DefterSatirSayisiAsync(sp));
        var ggSonra = await rs.GetGelirGiderAsync(null, null);
        Assert.Equal(ggOnce.GiderToplam, ggSonra.GiderToplam);
        Assert.Equal(ggOnce.GelirToplam, ggSonra.GelirToplam);
        Assert.Equal(ggOnce.NetKar, ggSonra.NetKar);
        var karneSonra = await rs.GetAracKarneAsync(arac);
        Assert.Equal(karneOnce.ToplamGider, karneSonra!.ToplamGider);
        Assert.Equal(karneOnce.ToplamGelir, karneSonra.ToplamGelir);
        Assert.Equal(karneOnce.ToplamNetKar, karneSonra.ToplamNetKar);
        Assert.Equal(servisMaliyetOnce.Single().Toplam, (await rs.GetServiceCostSummaryAsync(null, null)).Single().Toplam);
        // Servis kaydının işçilik toplamı da fatura alanlarından ETKİLENMEZ.
        Assert.Equal(400m, rec.ToplamIscilik);
        // Fatura/ödeme kaynaklı hiçbir defter satırı doğmadı.
        var fSp = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await fSp.CreateDbContextAsync();
        Assert.Empty(await db.AccountLedgerEntries.Where(e => e.SourceId == id).ToListAsync());
    }

    [Fact]
    public async Task Rucu_tabani_fatura_alanlarindan_ETKILENMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 07");
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rücu", Soyad = "Müşteri" });

        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = arac, Tip = ServisTipi.Ariza, HasarSorumlu = HasarSorumlu.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Onarım", BirimFiyat = 500m, Miktar = 2m, KdvOran = 0.20m }]
        });
        await svc.BaslatAsync(id);
        await svc.TamamlaAsync(id, cikisKm: 10);
        // Fatura KDV'li 1.200 olsa bile rücu tabanı KDV HARİÇ işçiliktir.
        Assert.True(await svc.BilgiGuncelleAsync(id, new ServiceRecordBilgiInput { FaturaTutar = 1_000m, FaturaKdv = 200m }));

        await svc.YansitAsync(id, cari);

        var rec = await svc.GetAsync(id);
        Assert.Equal(1_000m, rec!.ToplamIscilik);       // ELLE: 500 × 2 (KDV net'e karışmaz)
        Assert.Equal(500m, rec.YansitilanTutar);        // ELLE: 1.000 × 0,5 — 1.200 × 0,5 = 600 DEĞİL

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var satirlar = await db.AccountLedgerEntries.Where(e => e.SourceType == "ServisYansitma").ToListAsync();
        Assert.Equal(2, satirlar.Count);                                    // ELLE: borç + alacak
        Assert.All(satirlar, s => Assert.Equal(500m, s.Amount.Amount));
    }

    // ==================== E — doğrulama ====================

    [Fact]
    public async Task Sacma_degerler_temiz_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var arac = await AracAsync(sp, "34 SD 08");

        Task Red(Action<ServiceRecordInput> ayarla)
        {
            var i = new ServiceRecordInput { VehicleId = arac };
            ayarla(i);
            return Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(i));
        }

        await Red(i => i.FaturaTutar = -1m);
        await Red(i => i.FaturaKdv = -0.01m);
        await Red(i => i.Odeme = -5m);
        await Red(i => i.DegerKaybi = -1m);
        await Red(i => i.OdemeKur = 0m);                                   // kur pozitif olmalı
        await Red(i => i.OdemeDoviz = "TR");                               // 3 harf
        await Red(i => i.CikisYakit = 13);                                 // ölçek 0-12
        await Red(i => i.DonusYakit = -1);
        await Red(i => i.KazaTarihi = DateTimeOffset.UtcNow.AddDays(5));    // belge tarihi geleceğe yazılamaz
        await Red(i => i.FaturaTarihi = DateTimeOffset.UtcNow.AddDays(5));
        await Red(i => i.OdemeTarihi = DateTimeOffset.UtcNow.AddDays(5));
        await Red(i =>
        {
            i.PlanBasTarihi = DateTimeOffset.UtcNow.AddDays(5);
            i.PlanBitTarihi = DateTimeOffset.UtcNow.AddDays(4);             // bitiş < başlangıç
        });
        await Red(i => i.Lines = [new ServiceLineInput { Aciklama = "x", KdvOran = 1.5m }]);
        await Red(i => i.Lines = [new ServiceLineInput { Aciklama = "x", BirimFiyat = -1m }]);
        await Red(i => i.Lines = [new ServiceLineInput { Aciklama = "x", Miktar = -1m }]);
        await Red(i => i.Lines = [new ServiceLineInput { Aciklama = "x", Indirim = -1m }]);
        await Red(i => i.Lines = [new ServiceLineInput { Aciklama = "x" }]); // ne tutar ne birim fiyat
        // İndirim satır brütünü aşamaz: 100 × 1 − 500 = −400.
        await Red(i => i.Lines = [new ServiceLineInput { Aciklama = "x", BirimFiyat = 100m, Indirim = 500m }]);

        // Plan penceresi GELECEĞE açıktır (randevu) — reddedilmemeli.
        var ok = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = arac, Rezervasyon = true,
            PlanBasTarihi = DateTimeOffset.UtcNow.AddDays(30), PlanBitTarihi = DateTimeOffset.UtcNow.AddDays(31)
        });
        Assert.Equal(ServisDurum.Rezerve, (await svc.GetAsync(ok))!.Durum);
    }

    // ==================== F — yetki ====================

    [Fact]
    public async Task Yeni_uclar_OperationsWrite_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        Guid id;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            var arac = await AracAsync(sp, "34 SD 09");
            id = await sp.GetRequiredService<ServiceRecordService>()
                .CreateAsync(new ServiceRecordInput { VehicleId = arac, Rezervasyon = true });
        }

        // Muhasebe: FinanceWrite var, OperationsWrite YOK → operasyonel aksiyonlar reddedilir.
        using (var muhasebe = host.ScopeFor(tenant, Guid.NewGuid(), "muhasebeci", UserRole.Muhasebe))
        {
            var svc = muhasebe.ServiceProvider.GetRequiredService<ServiceRecordService>();
            await Assert.ThrowsAsync<YetkiYokException>(() => svc.ServiseAlAsync(id));
            await Assert.ThrowsAsync<YetkiYokException>(() =>
                svc.BilgiGuncelleAsync(id, new ServiceRecordBilgiInput { FaturaNo = "X" }));
        }

        // Operatör: OperationsWrite var → geçer.
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "operator", UserRole.Operator))
        {
            var svc = op.ServiceProvider.GetRequiredService<ServiceRecordService>();
            Assert.True(await svc.BilgiGuncelleAsync(id, new ServiceRecordBilgiInput { FaturaNo = "OP-1" }));
            Assert.True(await svc.ServiseAlAsync(id, girisKm: 10));
            Assert.Equal(ServisDurum.Acik, (await svc.GetAsync(id))!.Durum);
        }
    }

    // ==================== G — tenant izolasyonu (racar_app) ====================

    [Fact]
    public async Task Yeni_alanlar_tenant_izole_ham_rls_ile_dogrulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Guid kayitA;
        using (var sa = host.ScopeFor(a))
        {
            var arac = await AracAsync(sa.ServiceProvider, "34 IZ 01");
            var girdi = new ServiceRecordInput
            {
                VehicleId = arac, GirisKm = 100,
                Lines = [new ServiceLineInput { Aciklama = "Kalem", Tutar = 10m, KdvOran = 0.20m }]
            };
            BilgiDoldur(girdi);
            kayitA = await sa.ServiceProvider.GetRequiredService<ServiceRecordService>().CreateAsync(girdi);
        }

        using (var sb = host.ScopeFor(b))
        {
            var svcB = sb.ServiceProvider.GetRequiredService<ServiceRecordService>();
            Assert.Empty(await svcB.ListAsync());
            Assert.Null(await svcB.GetAsync(kayitA));
            // B, A'nın kaydının fatura bilgisini DEĞİŞTİREMEZ (satır görünmez → false).
            Assert.False(await svcB.BilgiGuncelleAsync(kayitA, new ServiceRecordBilgiInput { FaturaNo = "HACK" }));
            Assert.False(await svcB.ServiseAlAsync(kayitA));
        }

        // HAM RLS (racar_app): B GUC'uyla A'nın satırı yok, UPDATE 0 satır; A GUC'uyla 1 satır.
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        async Task SetTenant(Guid t)
        {
            await using var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn);
            set.Parameters.AddWithValue("t", t.ToString());
            await set.ExecuteScalarAsync();
        }

        await SetTenant(b);
        await using (var upd = new NpgsqlCommand(
            "update \"ServiceRecords\" set \"FaturaTutar\" = 1 where \"TenantId\" = @a", conn))
        {
            upd.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await upd.ExecuteNonQueryAsync());
        }

        await SetTenant(a);
        await using (var say = new NpgsqlCommand(
            "select count(*) from \"ServiceRecords\" where \"TenantId\" = @a and \"FaturaNo\" = 'SRV-FTR-9001'", conn))
        {
            say.Parameters.AddWithValue("a", a);
            Assert.Equal(1L, (long)(await say.ExecuteScalarAsync())!);
        }

        // DB değişmezleri (migration'a ELLE eklenen CHECK'ler) gerçekten açık mı — ampirik.
        // KdvOran bir ORANDIR: 1,5 yazmak (yani "%150" ya da "1,5 TL" niyeti) DB'de de reddedilir.
        await using (var kdv = new NpgsqlCommand(
            "update \"ServiceLines\" set \"KdvOran\" = 1.5 where \"TenantId\" = @a", conn))
        {
            kdv.Parameters.AddWithValue("a", a);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => kdv.ExecuteNonQueryAsync());
            Assert.Equal("23514", ex.SqlState); // check_violation
        }
        await using (var kur = new NpgsqlCommand(
            "update \"ServiceRecords\" set \"OdemeKur\" = 0 where \"TenantId\" = @a", conn))
        {
            kur.Parameters.AddWithValue("a", a);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => kur.ExecuteNonQueryAsync());
            Assert.Equal("23514", ex.SqlState); // check_violation
        }
    }
}
