using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-13 — araç kredisi zenginleştirme (canlı <c>arac_kredi.aspx</c> + <c>arac_kredi_listesi.aspx</c>).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler ELLE kurulan senaryodan yazılır, servisten
/// türetilmez. Örn. 12.000 TL · %10 yıllık basit faiz · 12 taksit → faiz 12000×0,10×12/12 = <b>1.200</b>,
/// toplam 13.200, aylık 1.100. 10.000 / 3 taksit (faizsiz) → 3333,33 + 3333,33 + <b>3333,34</b>.</para>
///
/// <para><b>DEFTERE YAZMAZ (KARARLAR.md genel politikası):</b> özet kartlar ve cari bağı salt
/// göstergedir. Kırılgan regresyon testi bunu ampirik kilitler: krediye cari bağlanıp taksit
/// ödendiğinde bile o carinin bakiyesi 0 kalır (para Gider/Kasa'ya gider, cariye DEĞİL).</para>
/// </summary>
[Collection("postgres")]
public sealed class AracKrediZenginlestirmeTests(PostgresFixture fx)
{
    /// <summary>Ayın ORTASI bilinçli: form tarihi yerel gece-yarısından UTC'ye çevrildiği için
    /// ayın 1'i/sonu seçmek, testin çalıştığı makinenin saat dilimine göre ay kaydırabilirdi.</summary>
    private static DateTimeOffset D(int y, int m, int g) => new(y, m, g, 0, 0, 0, TimeSpan.Zero);

    private static AracKredi Kredi(decimal tutar, decimal faiz, int taksit, DateTimeOffset bas,
        int odenen = 0, KrediDurum durum = KrediDurum.Aktif) => new()
    {
        No = "KR-TEST", BankaAdi = "Test Bank", KrediTutari = tutar, FaizOran = faiz,
        TaksitSayisi = taksit, BaslangicTarihi = bas, OdenenTaksit = odenen, Durum = durum
    };

    // ---------------------------------------------------------------- özet alanları (saf hesap)

    [Fact]
    public void Ozet_yeni_alanlari_ELLE_hesapla_ile_ayni()
    {
        // ELLE: 12.000 × %10 × 12/12 = 1.200 faiz → 13.200 toplam → 13.200/12 = 1.100 aylık.
        // Plan 15.03.2026'da başlar → 12. (son) vade 11 ay sonra = 15.02.2027.
        var k = Kredi(12_000m, 0.10m, 12, D(2026, 3, 15));

        var o = AracKrediService.Hesapla(k, D(2026, 5, 20));
        Assert.Equal(1_200m, o.ToplamFaiz);
        Assert.Equal(13_200m, o.ToplamGeriOdeme);
        Assert.Equal(1_100m, o.AylikTaksit);
        Assert.Equal(D(2027, 2, 15), o.SonVadeGunu);
        Assert.Equal(1_100m, o.SonTaksitTutari);     // bölünme tam → son taksit aylıkla aynı
        Assert.Equal(1_100m, o.BuAyToplamTaksit);    // mayısta 15.05 vadesi var

        // Planda vadesi olmayan bir ay → 0 (kart "bu ay ödeme yok" der).
        Assert.Equal(0m, AracKrediService.Hesapla(k, D(2028, 1, 10)).BuAyToplamTaksit);
    }

    [Fact]
    public void Son_taksit_kusurat_farkini_emer()
    {
        // ELLE: 10.000 / 3 = 3.333,333… → 3333,33 · 3333,33 · KALAN 3333,34; toplam TAM 10.000.
        var o = AracKrediService.Hesapla(Kredi(10_000m, 0m, 3, D(2026, 4, 15)), D(2026, 4, 20));
        Assert.Equal(3_333.33m, o.AylikTaksit);
        Assert.Equal(3_333.34m, o.SonTaksitTutari);
        Assert.Equal(10_000m, o.Taksitler.Sum(t => t.Tutar));   // kuruş kayması YOK
        Assert.Equal(D(2026, 6, 15), o.SonVadeGunu);
    }

    [Fact]
    public void Pano_5_kart_ELLE_hesapla_ile_ayni()
    {
        // ELLE kurulan küme:
        //  K1: 12.000 · %10 · 12 taksit · 15.03.2026 → faiz 1.200 · kalan 13.200 · aylık 1.100 · son vade 15.02.2027
        //  K2:  6.000 · %0  ·  6 taksit · 15.05.2026 → faiz     0 · kalan  6.000 · aylık 1.000 · son vade 15.10.2026
        //  K3: İPTAL (100.000) → hiçbir toplama girmez
        var k1 = Kredi(12_000m, 0.10m, 12, D(2026, 3, 15));
        var k2 = Kredi(6_000m, 0m, 6, D(2026, 5, 15));
        var k3 = Kredi(100_000m, 0.50m, 24, D(2026, 1, 15), durum: KrediDurum.Iptal);

        var p = AracKrediService.Pano([k1, k2, k3], D(2026, 5, 20));

        Assert.Equal(1_200m, p.ToplamFaiz);                 // 1.200 + 0 (iptal hariç)
        Assert.Equal(19_200m, p.ToplamKrediBorcu);          // 13.200 + 6.000
        Assert.Equal(2_100m, p.BuAyToplamTaksit);           // mayıs: 1.100 (K1) + 1.000 (K2)
        Assert.Equal(D(2027, 2, 15), p.SonVadeGunu);        // en geç biten kredi K1
        Assert.Equal(1_100m, p.SonTaksitTutari);            // O kredinin son taksiti
    }

    [Fact]
    public void Pano_bos_kumede_sifir()
    {
        var p = AracKrediService.Pano([], D(2026, 5, 20));
        Assert.Equal(0m, p.ToplamFaiz);
        Assert.Equal(0m, p.ToplamKrediBorcu);
        Assert.Null(p.SonVadeGunu);
    }

    [Fact]
    public void Odenen_taksit_kalan_borcu_dusurur_pano_da_dusurur()
    {
        // ELLE: 6.000 · 6 taksit faizsiz → aylık 1.000; 2 taksit ödenmiş → kalan 4.000.
        var k = Kredi(6_000m, 0m, 6, D(2026, 5, 15), odenen: 2);
        Assert.Equal(4_000m, AracKrediService.Hesapla(k, D(2026, 5, 20)).KalanBakiye);
        Assert.Equal(4_000m, AracKrediService.Pano([k], D(2026, 5, 20)).ToplamKrediBorcu);
        // "Bu ayki taksit" o ayın YÜKÜMLÜLÜĞÜdür — ödenmiş olması kutuyu değiştirmez.
        Assert.Equal(1_000m, AracKrediService.Pano([k], D(2026, 5, 20)).BuAyToplamTaksit);
    }

    // ---------------------------------------------------------------- filtre (SearchAsync)

    [Fact]
    public async Task SearchAsync_cari_plaka_dosya_tarih_filtreleri()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<AracKrediService>();

        var cariA = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kredi", Soyad = "Cari A" });
        var cariB = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kredi", Soyad = "Cari B" });
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KR 77" });

        // 3 kredi: yalnız BİRİ cariA'ya bağlı, yalnız BİRİ araca bağlı, dosya numaraları farklı.
        await svc.CreateAsync(new AracKrediInput
        {
            BankaAdi = "A Bank", CariId = cariA, VehicleId = arac, DosyaNo = "DS-100",
            KrediTutari = 10_000m, TaksitSayisi = 10, BaslangicTarihi = D(2026, 3, 15)
        });
        await svc.CreateAsync(new AracKrediInput
        {
            BankaAdi = "B Bank", CariId = cariB, DosyaNo = "DS-200",
            KrediTutari = 20_000m, TaksitSayisi = 10, BaslangicTarihi = D(2026, 6, 15)
        });
        await svc.CreateAsync(new AracKrediInput
        {
            BankaAdi = "C Bank", KrediTutari = 30_000m, TaksitSayisi = 10, BaslangicTarihi = D(2026, 9, 15)
        });

        Assert.Equal(3, (await svc.SearchAsync()).Count);                                   // filtresiz = hepsi
        Assert.Equal("A Bank", Assert.Single(await svc.SearchAsync(new AracKrediFilter { CariId = cariA })).BankaAdi);
        Assert.Equal("B Bank", Assert.Single(await svc.SearchAsync(new AracKrediFilter { CariId = cariB })).BankaAdi);

        // Plaka: kullanıcı BOŞLUKLU yazar, DB boşluksuz saklar → yine bulunmalı.
        Assert.Equal("A Bank", Assert.Single(await svc.SearchAsync(new AracKrediFilter { Plaka = "34 kr 77" })).BankaAdi);
        Assert.Equal("A Bank", Assert.Single(await svc.SearchAsync(new AracKrediFilter { Plaka = "KR77" })).BankaAdi);
        Assert.Empty(await svc.SearchAsync(new AracKrediFilter { Plaka = "06 XX 11" }));

        Assert.Equal("B Bank", Assert.Single(await svc.SearchAsync(new AracKrediFilter { DosyaNo = "200" })).BankaAdi);

        // Tarih aralığı BaslangicTarihi'ne uygulanır; 15.03 hariç, 15.06 ve 15.09 dahil.
        var aralik = await svc.SearchAsync(new AracKrediFilter { Bas = D(2026, 6, 1), Bit = D(2026, 12, 31) });
        Assert.Equal(2, aralik.Count);
        Assert.DoesNotContain(aralik, x => x.BankaAdi == "A Bank");

        // Durum filtresi
        Assert.Equal(3, (await svc.SearchAsync(new AracKrediFilter { Durum = KrediDurum.Aktif })).Count);
        Assert.Empty(await svc.SearchAsync(new AracKrediFilter { Durum = KrediDurum.Kapandi }));
    }

    [Fact]
    public async Task SearchAsync_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<AracKrediService>().CreateAsync(new AracKrediInput
            { BankaAdi = "Gizli Bank", DosyaNo = "DS-GIZLI", KrediTutari = 50_000m, TaksitSayisi = 6 });
        }
        // racar_app bağlantısı (fixture öyle kurulu) → RLS 2. tenant'a hiçbir satır sızdırmaz.
        using var b = host.ScopeFor(Guid.NewGuid());
        var svc = b.ServiceProvider.GetRequiredService<AracKrediService>();
        Assert.Empty(await svc.SearchAsync());
        Assert.Empty(await svc.SearchAsync(new AracKrediFilter { DosyaNo = "DS-GIZLI" }));
        Assert.Equal(0m, AracKrediService.Pano(await svc.SearchAsync(), DateTimeOffset.UtcNow).ToplamKrediBorcu);
    }

    // ---------------------------------------------------------------- toplu "Taksitleri İptal Et"

    [Fact]
    public async Task Toplu_iptal_yalniz_AKTIF_kredileri_kapatir_odenmis_gideri_SILMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<AracKrediService>();

        // A: aktif, hiç ödeme yok            → iptal edilmeli
        // B: 2 taksitin 2'si ödenmiş (Kapandi) → DOKUNULMAMALI
        // C: zaten iptal                      → sayaca girmemeli
        var a = await svc.CreateAsync(new AracKrediInput { BankaAdi = "A", KrediTutari = 1_000m, TaksitSayisi = 4 });
        var b = await svc.CreateAsync(new AracKrediInput { BankaAdi = "B", KrediTutari = 2_000m, TaksitSayisi = 2 });
        var c = await svc.CreateAsync(new AracKrediInput { BankaAdi = "C", KrediTutari = 3_000m, TaksitSayisi = 4 });
        await svc.TaksitOdeAsync(b);
        await svc.TaksitOdeAsync(b);          // B kapandı (2/2) — 2 × 1.000 gider postlandı (elle)
        await svc.IptalAsync(c);

        var giderOncesi = await sp.GetRequiredService<RentACar.Application.Expenses.ExpenseService>().ListAsync();
        Assert.Equal(2, giderOncesi.Count);

        Assert.Equal(1, await svc.TaksitleriIptalEtAsync([a, b, c]));

        Assert.Equal(KrediDurum.Iptal, (await svc.GetAsync(a))!.Durum);
        Assert.Equal(KrediDurum.Kapandi, (await svc.GetAsync(b))!.Durum);   // ödenmiş kredi geri alınmaz
        Assert.Equal(KrediDurum.Iptal, (await svc.GetAsync(c))!.Durum);

        // EN ÖNEMLİSİ: ödenmiş taksitlerin GİDER ve DEFTER kayıtları yerinde (para silinmez).
        var giderSonrasi = await sp.GetRequiredService<RentACar.Application.Expenses.ExpenseService>().ListAsync();
        Assert.Equal(2, giderSonrasi.Count);
        Assert.Equal(2_000m, giderSonrasi.Sum(x => x.GenelToplam));

        // İptal olan krediye artık taksit ödenemez (çit satır kilidinin arkasında).
        await Assert.ThrowsAsync<ValidationException>(() => svc.TaksitOdeAsync(a));

        // Boş seçim → net red (sessiz başarı yanıltıcı olurdu).
        await Assert.ThrowsAsync<ValidationException>(() => svc.TaksitleriIptalEtAsync([]));
    }

    [Fact]
    public async Task Toplu_iptal_YETKI_muhasebe_rolu_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id;
        using (var admin = host.ScopeFor(tenant))
        {
            id = await admin.ServiceProvider.GetRequiredService<AracKrediService>()
                .CreateAsync(new AracKrediInput { BankaAdi = "A", KrediTutari = 1_000m, TaksitSayisi = 4 });
        }

        // Muhasebe'de OperationsWrite YOK → toplu iptal reddedilmeli (okuma serbest).
        using (var muhasebe = host.ScopeFor(tenant, role: UserRole.Muhasebe))
        {
            var svc = muhasebe.ServiceProvider.GetRequiredService<AracKrediService>();
            Assert.Single(await svc.SearchAsync());
            await Assert.ThrowsAsync<ValidationException>(() => svc.TaksitleriIptalEtAsync([id]));
        }

        // Operatör'de OperationsWrite VAR → geçer.
        using (var op = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            Assert.Equal(1, await op.ServiceProvider.GetRequiredService<AracKrediService>()
                .TaksitleriIptalEtAsync([id]));
        }
    }

    // ---------------------------------------------------------------- "deftere yazmaz" kilidi

    [Fact]
    public async Task Cari_bagi_ve_ozet_kartlari_DEFTERE_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<AracKrediService>();
        var rapor = sp.GetRequiredService<ReportService>();

        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Kredi Bankası A.Ş." });

        // UÇUK bir kredi cariye bağlanır: 1.000.000 TL, %50 faiz.
        var id = await svc.CreateAsync(new AracKrediInput
        {
            BankaAdi = "Serbest Metin Banka", CariId = cari, DosyaNo = "DS-999",
            KrediTutari = 1_000_000m, FaizOran = 0.50m, TaksitSayisi = 12, BaslangicTarihi = D(2026, 4, 15)
        });

        // Özet kartlar dolu (ELLE: faiz 1.000.000 × 0,50 × 12/12 = 500.000).
        var o = AracKrediService.Hesapla((await svc.GetAsync(id))!, D(2026, 4, 20));
        Assert.Equal(500_000m, o.ToplamFaiz);
        Assert.Equal(1_500_000m, o.KalanBakiye);

        // …ama CARİ BAKİYESİ HİÇ OLUŞMADI: bakiye raporu boş (sıfır-bakiye cari listelenmez).
        Assert.Empty(await rapor.GetCariBalancesAsync());

        // Taksit ödendiğinde para Gider/Kasa'ya gider — cariye YİNE dokunulmaz.
        Assert.True(await svc.TaksitOdeAsync(id));
        Assert.Empty(await rapor.GetCariBalancesAsync());
    }
}
