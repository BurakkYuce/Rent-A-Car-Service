using RentACar.Application.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Pricing;

/// <summary>
/// Filo (uzun-dönem) maliyet hesaplayıcı (roadmap L2 + FAZ-74 kalem derinliği).
/// SALT-HESAP — defter/bakiye/kayıt YOK. QuoteCalculator (/fiyat-hesapla, günlük kira teklifi)
/// ile FARKLI; bu, araç satın alıp uzun-dönem kiralamanın maliyet/başabaş/kâr/teklif modeli.
/// Şeffaf "makul varsayım" formülleri (canlı kuruş paritesi değil).
///
/// <para><b>KALEM TOPLAMA KURALI (FAZ-74):</b> her gider kalemi kendi dönem tutarını üretir ve
/// <b>satır bazında</b> 2 haneye yuvarlanır; <c>ToplamGider</c> bu yuvarlanmış satırların
/// toplamıdır ve bir daha yuvarlanmaz. Ters sıra (önce topla, sonra yuvarla) kalem dökümüyle
/// toplamın kuruş tutmamasına yol açardı.</para>
/// </summary>
public static class MaliyetHesapService
{
    /// <summary>Rotatif seçildiğinde kullanıcıya dönen açık red mesajı (tek kaynak — test de bunu okur).</summary>
    public const string RotatifRedMesaji =
        "Rotatif (bakiye-azalan) kredi hesap yöntemi henüz uygulanmadı: faiz/amortisman formülü ayrı bir " +
        "finansal-model kararı bekliyor. Yanlış bir maliyet rakamı üretmemek için hesap reddedildi — " +
        "lütfen 'Eşit Taksitli' seçin.";

    /// <summary>Bir teklifte makul araç adedi üst sınırı (üstü veri girişi hatasıdır).</summary>
    public const int MaxAracSayisi = 1000;

    /// <summary>
    /// Tek gider kalemi için üst sınır (1 trilyon). İki nedenle var: (a) gerçek bir filo gideri
    /// bu büyüklükte olmaz — üstü veri girişi hatasıdır; (b) 120 ay × %300 enflasyon çarpanıyla
    /// birlikte sınırsız girdi <c>decimal</c> taşmasına (yakalanmamış OverflowException → 500)
    /// yol açardı. Ayrıca <c>numeric(19,4)</c> kolon sınırının altında kalır.
    /// </summary>
    public const decimal MaxKalemTutar = 1_000_000_000_000m;

    public static MaliyetHesapSonuc Hesapla(MaliyetHesapInput x)
    {
        // GÜVENLİ-RED, GİRİŞ NOKTASINDA: doğrulamalardan da önce. Rotatif bir girdi hatası değil,
        // "bu modelin cevabı yok" durumudur; alt formüllere hiç girilmemeli (KARARLAR.md FAZ-74).
        if (x.KrediHesaplamaSekli == KrediHesaplamaSekli.Rotatif)
            throw new ValidationException(RotatifRedMesaji);
        if (!Enum.IsDefined(x.KrediHesaplamaSekli))
            throw new ValidationException("Bilinmeyen kredi hesaplama şekli.");

        if (x.AlisBedeli <= 0m) throw new ValidationException("Alış bedeli pozitif olmalıdır.");
        if (x.SureAy is < 1 or > 120) throw new ValidationException("Süre (ay) 1 ile 120 arasında olmalıdır.");
        if (x.ResidualYuzde is < 0m or > 1m) throw new ValidationException("Kalıntı değer oranı 0-1 arası olmalıdır.");
        if (x.FaizOran < 0m || x.KkdfOran < 0m || x.BsmvOran < 0m || x.DamgaOran < 0m)
            throw new ValidationException("Oranlar negatif olamaz.");
        if (x.KarMarji < 0m) throw new ValidationException("Kâr marjı negatif olamaz.");
        if (x.KdvOran is < 0m or > 1m) throw new ValidationException("KDV oranı 0-1 arası olmalıdır.");
        // Enflasyon üst sınırı: %300/yıl. Sınırsız bırakılsaydı 120 ayda (1+oran)^9 ile tutarlar
        // anlamsız büyüklüklere taşardı.
        if (x.EnflasyonOran is < 0m or > 3m)
            throw new ValidationException("Enflasyon oranı 0 ile 3 (%300) arasında olmalıdır.");
        if (x.AracSayisi is < 1 or > MaxAracSayisi)
            throw new ValidationException($"Araç sayısı 1 ile {MaxAracSayisi} arasında olmalıdır.");

        var kalemler = KalemleriKur(x);   // negatif kalem doğrulaması burada

        var residualDeger = R(x.AlisBedeli * x.ResidualYuzde);
        var netAmortisman = x.AlisBedeli - residualDeger;                  // dönem boyu değer kaybı
        var finansmanFaiz = R(x.AlisBedeli * x.FaizOran * x.SureAy / 12m); // basit faiz, tam bedel (Eşit Taksitli)
        var finansmanVergi = R(finansmanFaiz * (x.KkdfOran + x.BsmvOran)); // faiz üzerinden KKDF+BSMV
        var damga = R(x.AlisBedeli * x.DamgaOran);

        // SATIR-BAZLI YUVARLAMA: her satır zaten 2 haneye kapalı → toplam da 2 hanede kapalıdır,
        // tekrar yuvarlanmaz (kalem dökümü ile toplam kuruşu kuruşuna tutar).
        var toplamGider = kalemler.Sum(k => k.DonemTutar);

        var toplamMaliyet = netAmortisman + finansmanFaiz + finansmanVergi + damga + toplamGider;
        var basaBasAylik = R(toplamMaliyet / x.SureAy);
        var kar = R(toplamMaliyet * x.KarMarji);
        var teklifNet = toplamMaliyet + kar;
        var teklifAylikNet = R(teklifNet / x.SureAy);
        var teklifKdvli = R(teklifNet * (1m + x.KdvOran));

        return new MaliyetHesapSonuc(
            residualDeger, netAmortisman, finansmanFaiz, finansmanVergi, damga, toplamGider,
            toplamMaliyet, basaBasAylik, kar, teklifNet, teklifAylikNet, teklifKdvli,
            kalemler, x.AracSayisi);
    }

    /// <summary>
    /// Gider kalemlerinin dönem dökümü. Tutarı 0 olan kalem listeye GİRMEZ (döküm okunur kalsın;
    /// toplama katkısı zaten 0'dır).
    /// </summary>
    private static List<MaliyetGiderKalem> KalemleriKur(MaliyetHesapInput x)
    {
        // Ay faktörü: enflasyon eskalasyonuyla "kaç aylık birim" ödendiğini verir.
        // Eskalasyon YOK ise faktör = SureAy → eski formül birebir korunur.
        var ayFaktor = AyFaktor(x.EnflasyonOran, x.SureAy);

        var ham = new (string Ad, MaliyetKalemPeriyot Periyot, decimal Birim)[]
        {
            ("Kasko",                 MaliyetKalemPeriyot.Yillik, x.KaskoYillik),
            ("Trafik Sigortası",      MaliyetKalemPeriyot.Yillik, x.TrafikSigortasiYillik),
            ("MTV",                   MaliyetKalemPeriyot.Yillik, x.MtvYillik),
            ("Bakım",                 MaliyetKalemPeriyot.Yillik, x.BakimYillik),
            ("Lastik (yaz)",          MaliyetKalemPeriyot.Yillik, x.LastikYillik),
            ("Lastik (kış)",          MaliyetKalemPeriyot.Yillik, x.LastikKisYillik),
            ("Araç Takip",            MaliyetKalemPeriyot.Yillik, x.AracTakipYillik),
            ("Tescil / Plaka",        MaliyetKalemPeriyot.Yillik, x.TescilPlakaYillik),
            ("Muayene / Emisyon",     MaliyetKalemPeriyot.Yillik, x.MuayeneEmisyonYillik),
            ("Yedek Araç",            MaliyetKalemPeriyot.Yillik, x.YedekAracYillik),
            ("Yönetim Gideri",        MaliyetKalemPeriyot.Aylik,  x.YonetimGideriAylik),
            ("Diğer (aylık)",         MaliyetKalemPeriyot.Aylik,  x.AylikGider),
            ("Banka Dosya / Diğer Masraf", MaliyetKalemPeriyot.TekSeferlik, x.BankaDosyaDigerMasraf)
        };

        var liste = new List<MaliyetGiderKalem>(ham.Length);
        foreach (var (ad, periyot, birim) in ham)
        {
            // Negatif kalem "gizli indirim" olurdu; tek tek reddedilir ki kullanıcı HANGİ kalemi
            // düzelteceğini bilsin (toplu "gider negatif olamaz" mesajı yol göstermezdi).
            if (birim < 0m) throw new ValidationException($"{ad} gideri negatif olamaz.");
            if (birim > MaxKalemTutar)
                throw new ValidationException($"{ad} gideri çok büyük (üst sınır {MaxKalemTutar:N0}).");
            if (birim == 0m) continue;

            var donemTutar = periyot switch
            {
                MaliyetKalemPeriyot.Yillik => R(birim / 12m * ayFaktor),
                MaliyetKalemPeriyot.Aylik => R(birim * ayFaktor),
                _ => R(birim)   // TekSeferlik — süreyle ÇARPILMAZ, enflasyondan etkilenmez
            };
            liste.Add(new MaliyetGiderKalem(ad, periyot, birim, donemTutar));
        }
        return liste;
    }

    /// <summary>
    /// Tekrarlayan kalemler için "etkin ay sayısı". Enflasyon YIL BASAMAKLI uygulanır:
    /// 1-12. ay ×1, 13-24. ay ×(1+e), 25-36. ay ×(1+e)² …
    /// Örn. e=0.20, 24 ay → 12×1 + 12×1.20 = 26.4 (elle doğrulanabilir).
    /// e=0 → faktör tam olarak SureAy'dır; eski formül DEĞİŞMEZ.
    /// </summary>
    public static decimal AyFaktor(decimal enflasyonOran, int sureAy)
    {
        if (enflasyonOran == 0m) return sureAy;

        decimal toplam = 0m, yilCarpan = 1m;
        for (var ay = 1; ay <= sureAy; ay++)
        {
            if (ay > 1 && (ay - 1) % 12 == 0) yilCarpan *= 1m + enflasyonOran;
            toplam += yilCarpan;
        }
        return toplam;
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
