using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-74 — kalem-bazlı maliyet girdisi (salt-hesap, DB yok).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler bu dosyada ELLE hesaplanmıştır; hiçbiri
/// <see cref="CostCalculationService"/> çağrılarak üretilmez. Her sabitin yanında aritmetiği yazılıdır.</para>
///
/// <para><b>Kapsam:</b> (1) kalem toplama matematiği, (2) satır-bazlı yuvarlama, (3) tek-seferlik
/// kalemin süreyle çarpılmaması, (4) enflasyon eskalasyonu, (5) geriye uyum (FAZ-74 öncesi formül
/// birebir korunur), (6) Rotatif güvenli-red, (7) sınır doğrulamaları.</para>
/// </summary>
public sealed class MaliyetHesapKalemTests
{
    /// <summary>Yalnız kalem alanlarını sınamak için taban: 1.000.000 alış, %30 kalıntı, finansmansız.</summary>
    private static MaliyetHesapInput Taban(int sureAy = 36) => new()
    {
        AlisBedeli = 1_000_000m,
        ResidualYuzde = 0.30m,
        SureAy = sureAy,
        FaizOran = 0m,
        KkdfOran = 0m,
        BsmvOran = 0m,
        DamgaOran = 0m,
        KarMarji = 0m,
        KdvOran = 0.20m
    };

    [Fact]
    public void Kalemler_donem_toplamina_ELLE_hesaplanan_degerle_yayilir()
    {
        var x = Taban(sureAy: 36);
        x.KaskoYillik = 12_000m;             // 12000/12 × 36 =  36.000
        x.TrafikSigortasiYillik = 6_000m;    //  6000/12 × 36 =  18.000
        x.MtvYillik = 3_600m;                //  3600/12 × 36 =  10.800
        x.BakimYillik = 9_000m;              //  9000/12 × 36 =  27.000
        x.LastikYillik = 4_800m;             //  4800/12 × 36 =  14.400
        x.LastikKisYillik = 5_400m;          //  5400/12 × 36 =  16.200
        x.AracTakipYillik = 1_200m;          //  1200/12 × 36 =   3.600
        x.TescilPlakaYillik = 2_400m;        //  2400/12 × 36 =   7.200
        x.MuayeneEmisyonYillik = 1_800m;     //  1800/12 × 36 =   5.400
        x.YedekAracYillik = 3_000m;          //  3000/12 × 36 =   9.000
        x.YonetimGideriAylik = 500m;         //   500 × 36     =  18.000
        x.AylikGider = 250m;                 //   250 × 36     =   9.000  (geriye uyum: "Diğer")
        x.BankaDosyaDigerMasraf = 7_500m;    //   tek seferlik =   7.500
        //                                      TOPLAM        = 182.100

        var s = CostCalculationService.Calculate(x);

        Assert.Equal(182_100m, s.ToplamGider);
        Assert.Equal(13, s.Kalemler.Count);   // 13 kalem girildi, 13'ü de dökümde
        Assert.Equal(36_000m, s.Kalemler.Single(k => k.Ad == "Kasko").DonemTutar);
        Assert.Equal(18_000m, s.Kalemler.Single(k => k.Ad == "Yönetim Gideri").DonemTutar);
        Assert.Equal(9_000m, s.Kalemler.Single(k => k.Ad == "Diğer (aylık)").DonemTutar);
        Assert.Equal(7_500m, s.Kalemler.Single(k => k.Ad.StartsWith("Banka Dosya")).DonemTutar);

        // Döküm ile toplam KURUŞU KURUŞUNA tutar (satır-bazlı yuvarlamanın anlamı budur).
        Assert.Equal(s.ToplamGider, s.Kalemler.Sum(k => k.DonemTutar));

        // ELLE: kalıntı 300.000 → amortisman 700.000; toplam maliyet 700.000 + 182.100 = 882.100
        Assert.Equal(300_000m, s.ResidualDeger);
        Assert.Equal(700_000m, s.NetAmortisman);
        Assert.Equal(882_100m, s.ToplamMaliyet);
        Assert.Equal(24_502.78m, s.BasaBasAylik);     // 882100/36 = 24502,777… → 24502,78
        Assert.Equal(882_100m, s.TeklifNet);          // kâr marjı 0
        Assert.Equal(1_058_520m, s.TeklifKdvli);      // 882100 × 1,20
    }

    [Fact]
    public void Yuvarlama_SATIR_BAZINDA_yapilir_toplam_sonradan_yuvarlanmaz()
    {
        // ELLE: 7 aylık dönemde 100 TL/yıl kalem → 100/12 × 7 = 58,3333… → satır 58,33.
        // ÜÇ ayrı kalem → 58,33 × 3 = 174,99.
        // Yanlış sıra (önce topla, sonra yuvarla) 300/12 × 7 = 175,00 verirdi — 1 kuruş fark
        // bu testin TAM ayırt ettiği şeydir.
        var x = Taban(sureAy: 7);
        x.KaskoYillik = 100m;
        x.TrafikSigortasiYillik = 100m;
        x.MtvYillik = 100m;

        var s = CostCalculationService.Calculate(x);

        Assert.All(s.Kalemler, k => Assert.Equal(58.33m, k.DonemTutar));
        Assert.Equal(174.99m, s.ToplamGider);
        Assert.NotEqual(175.00m, s.ToplamGider);
    }

    [Fact]
    public void Tek_seferlik_kalem_sureyle_CARPILMAZ()
    {
        // ELLE: banka dosya masrafı 10.000 → 36 aylık dönemde de 10.000'dir.
        // (Aylığa bölüp süreyle çarpan bir model 36 kez dosya masrafı yazardı.)
        var x = Taban(sureAy: 36);
        x.BankaDosyaDigerMasraf = 10_000m;

        var s = CostCalculationService.Calculate(x);

        Assert.Equal(10_000m, s.ToplamGider);
        var kalem = Assert.Single(s.Kalemler);
        Assert.Equal(CostItemPeriod.TekSeferlik, kalem.Periyot);
        Assert.Equal(10_000m, kalem.DonemTutar);

        // Aynı tutar AYLIK kalem olarak girilseydi 36 katı olurdu — iki periyodun farkı ampirik.
        var y = Taban(sureAy: 36);
        y.YonetimGideriAylik = 10_000m;
        Assert.Equal(360_000m, CostCalculationService.Calculate(y).ToplamGider);
    }

    [Fact]
    public void Enflasyon_YIL_BASAMAKLI_uygulanir_tek_seferlik_kalemi_ETKILEMEZ()
    {
        // ELLE ay-faktörü (24 ay, %20): ilk 12 ay ×1 + sonraki 12 ay ×1,20 = 12 + 14,4 = 26,4
        Assert.Equal(26.4m, CostCalculationService.MonthFactor(0.20m, 24));
        // 36 ay: 12 + 12×1,20 + 12×1,44 = 12 + 14,4 + 17,28 = 43,68
        Assert.Equal(43.68m, CostCalculationService.MonthFactor(0.20m, 36));
        // Eskalasyon KAPALI iken faktör tam olarak ay sayısıdır (eski formül korunur).
        Assert.Equal(36m, CostCalculationService.MonthFactor(0m, 36));

        var x = Taban(sureAy: 24);
        x.EnflasyonOran = 0.20m;
        x.KaskoYillik = 12_000m;          // 12000/12 × 26,4 = 1000 × 26,4 = 26.400
        x.YonetimGideriAylik = 500m;      //   500 × 26,4                  = 13.200
        x.BankaDosyaDigerMasraf = 5_000m; //   tek seferlik, ESKALASYONSUZ =  5.000
        //                                   TOPLAM                        = 44.600

        var s = CostCalculationService.Calculate(x);

        Assert.Equal(26_400m, s.Kalemler.Single(k => k.Ad == "Kasko").DonemTutar);
        Assert.Equal(13_200m, s.Kalemler.Single(k => k.Ad == "Yönetim Gideri").DonemTutar);
        Assert.Equal(5_000m, s.Kalemler.Single(k => k.Ad.StartsWith("Banka Dosya")).DonemTutar);
        Assert.Equal(44_600m, s.ToplamGider);

        // Enflasyon 0 iken AYNI girdi: 12000/12×24 + 500×24 + 5000 = 24.000 + 12.000 + 5.000 = 41.000
        x.EnflasyonOran = 0m;
        Assert.Equal(41_000m, CostCalculationService.Calculate(x).ToplamGider);
    }

    [Fact]
    public void GERIYE_UYUM_yalniz_AylikGider_girilince_FAZ74_oncesi_formul_birebir_ayni()
    {
        // ELLE (FAZ-74 ÖNCESİ formül, tamamen elde):
        //   kalıntı        = 1.000.000 × 0,30            =   300.000
        //   net amortisman = 1.000.000 − 300.000         =   700.000
        //   faiz           = 1.000.000 × 0,40 × 36/12    = 1.200.000
        //   faiz vergisi   = 1.200.000 × (0,15+0,15)     =   360.000
        //   damga          = 1.000.000 × 0,001           =     1.000
        //   gider          = 1.000 × 36                  =    36.000
        //   toplam maliyet                               = 2.297.000
        //   başabaş/ay     = 2.297.000 / 36              =    63.805,56
        //   kâr            = 2.297.000 × 0,20            =   459.400
        //   teklif net                                   = 2.756.400
        //   teklif aylık   = 2.756.400 / 36              =    76.566,67
        //   teklif KDV'li  = 2.756.400 × 1,20            = 3.307.680
        var s = CostCalculationService.Calculate(new MaliyetHesapInput
        {
            AlisBedeli = 1_000_000m, ResidualYuzde = 0.30m, SureAy = 36,
            FaizOran = 0.40m, KkdfOran = 0.15m, BsmvOran = 0.15m, DamgaOran = 0.001m,
            AylikGider = 1_000m, KarMarji = 0.20m, KdvOran = 0.20m
        });

        Assert.Equal(300_000m, s.ResidualDeger);
        Assert.Equal(700_000m, s.NetAmortisman);
        Assert.Equal(1_200_000m, s.FinansmanFaiz);
        Assert.Equal(360_000m, s.FinansmanVergi);
        Assert.Equal(1_000m, s.Damga);
        Assert.Equal(36_000m, s.ToplamGider);
        Assert.Equal(2_297_000m, s.ToplamMaliyet);
        Assert.Equal(63_805.56m, s.BasaBasAylik);
        Assert.Equal(459_400m, s.Kar);
        Assert.Equal(2_756_400m, s.TeklifNet);
        Assert.Equal(76_566.67m, s.TeklifAylikNet);
        Assert.Equal(3_307_680m, s.TeklifKdvli);

        // AylikGider artık "Diğer (aylık)" KALEMİDİR — silinmedi, dökümde görünür.
        var kalem = Assert.Single(s.Kalemler);
        Assert.Equal("Diğer (aylık)", kalem.Ad);
        Assert.Equal(CostItemPeriod.Aylik, kalem.Periyot);
    }

    [Fact]
    public void ROTATIF_guvenli_red_sessiz_yanlis_hesap_YOK()
    {
        var x = Taban();
        x.FaizOran = 0.40m;
        x.KrediHesaplamaSekli = LoanCalculationMethod.Rotatif;

        var ex = Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(x));
        // Mesaj kullanıcıya NE olduğunu ve NE yapması gerektiğini söylemeli.
        Assert.Contains("Rotatif", ex.Message);
        Assert.Contains("Eşit Taksitli", ex.Message);
        Assert.Equal(CostCalculationService.RevolvingRejectMessage, ex.Message);

        // AYNI girdi Eşit Taksitli ile SORUNSUZ hesaplanır → red, girdinin geçersizliğinden değil
        // yöntemin uygulanmamış olmasından gelir.
        x.KrediHesaplamaSekli = LoanCalculationMethod.EsitTaksitli;
        Assert.Equal(1_200_000m, CostCalculationService.Calculate(x).FinansmanFaiz);   // 1.000.000×0,40×3

        // Rotatif reddi, DİĞER doğrulamalardan ÖNCE gelir: alış bedeli geçersiz olsa bile
        // kullanıcı "rotatif yok" cevabını alır (giriş noktası guard'ı).
        var bozuk = new MaliyetHesapInput { AlisBedeli = 0m, KrediHesaplamaSekli = LoanCalculationMethod.Rotatif };
        Assert.Equal(CostCalculationService.RevolvingRejectMessage,
            Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(bozuk)).Message);
    }

    [Fact]
    public void Arac_sayisi_filo_toplamlarini_TAM_KAT_uretir()
    {
        var x = Taban(sureAy: 36);
        x.KaskoYillik = 12_000m;    // 36.000 → toplam maliyet 700.000 + 36.000 = 736.000
        x.AracSayisi = 10;

        var s = CostCalculationService.Calculate(x);

        Assert.Equal(736_000m, s.ToplamMaliyet);          // ARAÇ BAŞINA
        Assert.Equal(10, s.AracSayisi);
        Assert.Equal(7_360_000m, s.FiloToplamMaliyet);    // 736.000 × 10
        // ELLE: aylık net = 736.000/36 = 20.444,4444… → 20.444,44 ; filo = ×10 = 204.444,40
        Assert.Equal(20_444.44m, s.TeklifAylikNet);
        Assert.Equal(204_444.40m, s.FiloTeklifAylikNet);

        // Araç sayısı hesabın KENDİSİNİ değiştirmez (girdi baştan çarpılmıyor).
        x.AracSayisi = 1;
        Assert.Equal(736_000m, CostCalculationService.Calculate(x).ToplamMaliyet);
    }

    [Fact]
    public void Sinir_ve_isaret_dogrulamalari()
    {
        // Negatif kalem HANGİ kalem olduğu söylenerek reddedilir (toplu mesaj yol göstermezdi).
        var neg = Taban();
        neg.KaskoYillik = -1m;
        Assert.Contains("Kasko", Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(neg)).Message);

        var neg2 = Taban();
        neg2.BankaDosyaDigerMasraf = -0.01m;
        Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(neg2));

        // Enflasyon: negatif ve %300 üstü reddedilir (üst sınır 120 ayda taşmayı engeller).
        var e1 = Taban(); e1.EnflasyonOran = -0.01m;
        Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(e1));
        var e2 = Taban(); e2.EnflasyonOran = 3.01m;
        Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(e2));
        var e3 = Taban(); e3.EnflasyonOran = 3m;
        CostCalculationService.Calculate(e3);   // tam sınır GEÇERLİ

        // Kalem üst sınırı: sınırsız girdi 120 ay × %300 enflasyonla decimal TAŞMASI üretirdi
        // (yakalanmamış OverflowException → 500). Temiz red gelir.
        var buyuk = Taban();
        buyuk.KaskoYillik = CostCalculationService.MaxItemAmount + 1m;
        Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(buyuk));
        var sinir = Taban(sureAy: 1);
        sinir.KaskoYillik = CostCalculationService.MaxItemAmount;
        CostCalculationService.Calculate(sinir);   // tam sınır GEÇERLİ

        // Araç sayısı 1..1000
        var a0 = Taban(); a0.AracSayisi = 0;
        Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(a0));
        var a1 = Taban(); a1.AracSayisi = CostCalculationService.MaxVehicleCount + 1;
        Assert.Throws<ValidationException>(() => CostCalculationService.Calculate(a1));

        // Sıfır tutarlı kalem dökümde YER ALMAZ (döküm okunur kalsın; toplama katkısı zaten 0).
        var sifir = Taban();
        sifir.KaskoYillik = 0m;
        Assert.Empty(CostCalculationService.Calculate(sifir).Kalemler);
        Assert.Equal(0m, CostCalculationService.Calculate(sifir).ToplamGider);
    }

    [Fact]
    public void Uzun_donem_120_ay_yuksek_enflasyonda_TASMAZ()
    {
        // int-taşma/aşırı büyüme dersi: 120 ay × %300 enflasyon en uç senaryodur; hesap decimal
        // kalmalı ve patlamamalıdır. Faktör ELLE: Σ_{y=0..9} 12×4^y = 12 × (4^10 − 1)/3
        // = 12 × 1.048.575/3 = 12 × 349.525 = 4.194.300
        Assert.Equal(4_194_300m, CostCalculationService.MonthFactor(3m, 120));

        var x = Taban(sureAy: 120);
        x.EnflasyonOran = 3m;
        x.YonetimGideriAylik = 1m;    // 1 × 4.194.300 = 4.194.300
        var s = CostCalculationService.Calculate(x);
        Assert.Equal(4_194_300m, s.ToplamGider);
    }
}
