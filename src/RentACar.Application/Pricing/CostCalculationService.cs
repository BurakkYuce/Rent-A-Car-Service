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
public static class CostCalculationService
{
    /// <summary>Rotatif seçildiğinde kullanıcıya dönen açık red mesajı (tek kaynak — test de bunu okur).</summary>
    public const string RevolvingRejectMessage =
        "Rotatif (bakiye-azalan) kredi hesap yöntemi henüz uygulanmadı: faiz/amortisman formülü ayrı bir " +
        "finansal-model kararı bekliyor. Yanlış bir maliyet rakamı üretmemek için hesap reddedildi — " +
        "lütfen 'Eşit Taksitli' seçin.";

    /// <summary>Bir teklifte makul araç adedi üst sınırı (üstü veri girişi hatasıdır).</summary>
    public const int MaxVehicleCount = 1000;

    /// <summary>
    /// Tek gider kalemi için üst sınır (1 trilyon). İki nedenle var: (a) gerçek bir filo gideri
    /// bu büyüklükte olmaz — üstü veri girişi hatasıdır; (b) 120 ay × %300 enflasyon çarpanıyla
    /// birlikte sınırsız girdi <c>decimal</c> taşmasına (yakalanmamış OverflowException → 500)
    /// yol açardı. Ayrıca <c>numeric(19,4)</c> kolon sınırının altında kalır.
    /// </summary>
    public const decimal MaxItemAmount = 1_000_000_000_000m;

    public static MaliyetHesapSonuc Calculate(MaliyetHesapInput x)
    {
        // GÜVENLİ-RED, GİRİŞ NOKTASINDA: doğrulamalardan da önce. Rotatif bir girdi hatası değil,
        // "bu modelin cevabı yok" durumudur; alt formüllere hiç girilmemeli (KARARLAR.md FAZ-74).
        if (x.KrediHesaplamaSekli == LoanCalculationMethod.Rotatif)
            throw new ValidationException(RevolvingRejectMessage);
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
        if (x.AracSayisi is < 1 or > MaxVehicleCount)
            throw new ValidationException($"Araç sayısı 1 ile {MaxVehicleCount} arasında olmalıdır.");

        var items = BuildItems(x);   // negatif kalem doğrulaması burada

        var residualValue = R(x.AlisBedeli * x.ResidualYuzde);
        var netDepreciation = x.AlisBedeli - residualValue;                  // dönem boyu değer kaybı
        var financingInterest = R(x.AlisBedeli * x.FaizOran * x.SureAy / 12m); // basit faiz, tam bedel (Eşit Taksitli)
        var financingTax = R(financingInterest * (x.KkdfOran + x.BsmvOran)); // faiz üzerinden KKDF+BSMV
        var stamp = R(x.AlisBedeli * x.DamgaOran);

        // SATIR-BAZLI YUVARLAMA: her satır zaten 2 haneye kapalı → toplam da 2 hanede kapalıdır,
        // tekrar yuvarlanmaz (kalem dökümü ile toplam kuruşu kuruşuna tutar).
        var totalExpense = items.Sum(k => k.DonemTutar);

        var totalCost = netDepreciation + financingInterest + financingTax + stamp + totalExpense;
        var breakEvenMonthly = R(totalCost / x.SureAy);
        var profit = R(totalCost * x.KarMarji);
        var quoteNet = totalCost + profit;
        var quoteMonthlyNet = R(quoteNet / x.SureAy);
        var quoteWithVat = R(quoteNet * (1m + x.KdvOran));

        return new MaliyetHesapSonuc(
            residualValue, netDepreciation, financingInterest, financingTax, stamp, totalExpense,
            totalCost, breakEvenMonthly, profit, quoteNet, quoteMonthlyNet, quoteWithVat,
            items, x.AracSayisi);
    }

    /// <summary>
    /// Gider kalemlerinin dönem dökümü. Tutarı 0 olan kalem listeye GİRMEZ (döküm okunur kalsın;
    /// toplama katkısı zaten 0'dır).
    /// </summary>
    private static List<MaliyetGiderKalem> BuildItems(MaliyetHesapInput x)
    {
        // Ay faktörü: enflasyon eskalasyonuyla "kaç aylık birim" ödendiğini verir.
        // Eskalasyon YOK ise faktör = SureAy → eski formül birebir korunur.
        var monthFactor = MonthFactor(x.EnflasyonOran, x.SureAy);

        var raw = new (string Ad, CostItemPeriod Periyot, decimal Birim)[]
        {
            ("Kasko",                 CostItemPeriod.Yillik, x.KaskoYillik),
            ("Trafik Sigortası",      CostItemPeriod.Yillik, x.TrafikSigortasiYillik),
            ("MTV",                   CostItemPeriod.Yillik, x.MtvYillik),
            ("Bakım",                 CostItemPeriod.Yillik, x.BakimYillik),
            ("Lastik (yaz)",          CostItemPeriod.Yillik, x.LastikYillik),
            ("Lastik (kış)",          CostItemPeriod.Yillik, x.LastikKisYillik),
            ("Araç Takip",            CostItemPeriod.Yillik, x.AracTakipYillik),
            ("Tescil / Plaka",        CostItemPeriod.Yillik, x.TescilPlakaYillik),
            ("Muayene / Emisyon",     CostItemPeriod.Yillik, x.MuayeneEmisyonYillik),
            ("Yedek Araç",            CostItemPeriod.Yillik, x.YedekAracYillik),
            ("Yönetim Gideri",        CostItemPeriod.Aylik,  x.YonetimGideriAylik),
            ("Diğer (aylık)",         CostItemPeriod.Aylik,  x.AylikGider),
            ("Banka Dosya / Diğer Masraf", CostItemPeriod.TekSeferlik, x.BankaDosyaDigerMasraf)
        };

        var list = new List<MaliyetGiderKalem>(raw.Length);
        foreach (var (name, period, unit) in raw)
        {
            // Negatif kalem "gizli indirim" olurdu; tek tek reddedilir ki kullanıcı HANGİ kalemi
            // düzelteceğini bilsin (toplu "gider negatif olamaz" mesajı yol göstermezdi).
            if (unit < 0m) throw new ValidationException($"{name} gideri negatif olamaz.");
            if (unit > MaxItemAmount)
                throw new ValidationException($"{name} gideri çok büyük (üst sınır {MaxItemAmount:N0}).");
            if (unit == 0m) continue;

            var periodAmount = period switch
            {
                CostItemPeriod.Yillik => R(unit / 12m * monthFactor),
                CostItemPeriod.Aylik => R(unit * monthFactor),
                _ => R(unit)   // TekSeferlik — süreyle ÇARPILMAZ, enflasyondan etkilenmez
            };
            list.Add(new MaliyetGiderKalem(name, period, unit, periodAmount));
        }
        return list;
    }

    /// <summary>
    /// Tekrarlayan kalemler için "etkin ay sayısı". Enflasyon YIL BASAMAKLI uygulanır:
    /// 1-12. ay ×1, 13-24. ay ×(1+e), 25-36. ay ×(1+e)² …
    /// Örn. e=0.20, 24 ay → 12×1 + 12×1.20 = 26.4 (elle doğrulanabilir).
    /// e=0 → faktör tam olarak SureAy'dır; eski formül DEĞİŞMEZ.
    /// </summary>
    public static decimal MonthFactor(decimal inflationRate, int durationMonths)
    {
        if (inflationRate == 0m) return durationMonths;

        decimal total = 0m, yearMultiplier = 1m;
        for (var month = 1; month <= durationMonths; month++)
        {
            if (month > 1 && (month - 1) % 12 == 0) yearMultiplier *= 1m + inflationRate;
            total += yearMultiplier;
        }
        return total;
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
