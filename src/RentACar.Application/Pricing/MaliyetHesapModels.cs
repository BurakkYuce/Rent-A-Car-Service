using RentACar.Domain.Enums;

namespace RentACar.Application.Pricing;

/// <summary>
/// Filo (uzun-dönem) maliyet hesaplayıcı girişi (roadmap L2 + FAZ-74 kalem derinliği).
/// Oranlar kesir (0.30 = %30). MAKUL VARSAYIM modeli — canlı referans sistem kuruş paritesi DEĞİL.
///
/// <para><b>SALT-HESAP:</b> bu girdiden üretilen hiçbir tutar muhasebe defterine yazılmaz.
/// Ekran bir simülasyon/planlama aracıdır; gerçek para hareketi Kasa/Banka akışından geçer
/// (KARARLAR.md "GENEL POLİTİKA — yeni tutar alanları deftere yazmaz").</para>
///
/// <para><b>TEK PARA BİRİMİ:</b> modelde döviz/kur alanı YOKTUR — tüm tutarlar aynı (baz) para
/// biriminde varsayılır ve öyle toplanır. Farklı dövizli tutarları bu alanlara karıştırmak
/// anlamsız bir toplam üretir; çok-dövizli teklif gerekirse tutarlar önce baza çevrilmelidir
/// (<c>Money.AmountInBase</c> deseni).</para>
/// </summary>
public sealed class MaliyetHesapInput
{
    // ---------------- taban (roadmap L2, DEĞİŞMEDİ) ----------------
    public decimal AlisBedeli { get; set; }
    public decimal ResidualYuzde { get; set; } = 0.30m;   // dönem sonu kalıntı değer oranı
    public int SureAy { get; set; } = 36;
    public decimal FaizOran { get; set; }                 // yıllık basit faiz (kesir)
    public decimal KkdfOran { get; set; } = 0.15m;        // faiz üzerinden KKDF
    public decimal BsmvOran { get; set; } = 0.15m;        // faiz üzerinden BSMV
    public decimal DamgaOran { get; set; }                // alış bedeli üzerinden damga
    public decimal KarMarji { get; set; } = 0.20m;        // toplam maliyet üzerine kâr
    public decimal KdvOran { get; set; } = 0.20m;

    /// <summary>
    /// GERİYE UYUM (roadmap L2'nin tek gider alanı). FAZ-74'te silinmedi: artık "Diğer (aylık)"
    /// gider KALEMİ olarak toplama girer. Diğer kalemler 0 bırakılırsa sonuç FAZ-74 öncesiyle
    /// birebir AYNI çıkar (regresyon testiyle kilitli).
    /// </summary>
    public decimal AylikGider { get; set; }

    // ---------------- FAZ-74: kalem-bazlı işletme gideri ----------------
    // YILLIK kalemler — dönem tutarı = birim/12 × ay-faktörü (satır bazında yuvarlanır).
    public decimal KaskoYillik { get; set; }
    public decimal TrafikSigortasiYillik { get; set; }
    public decimal MtvYillik { get; set; }
    public decimal BakimYillik { get; set; }
    public decimal LastikYillik { get; set; }
    public decimal LastikKisYillik { get; set; }
    public decimal AracTakipYillik { get; set; }
    public decimal TescilPlakaYillik { get; set; }
    public decimal MuayeneEmisyonYillik { get; set; }
    public decimal YedekAracYillik { get; set; }

    /// <summary>AYLIK kalem — yönetim/genel gider payı.</summary>
    public decimal YonetimGideriAylik { get; set; }

    /// <summary>
    /// TEK SEFERLİK kalem — banka dosya masrafı, ekspertiz, noter vb.
    /// <b>Dönem boyunca TEKRARLAMAZ.</b> Aylığa bölüp süreyle çarpmak (36 ayda 36 kez dosya
    /// masrafı) uydurma maliyet üretirdi; bu yüzden tutar döneme BİR KEZ girer.
    /// </summary>
    public decimal BankaDosyaDigerMasraf { get; set; }

    /// <summary>
    /// Yıllık enflasyon/eskalasyon ORANI (kesir; 0.35 = %35). İşletme gideri kalemlerine —
    /// yalnız tekrarlayanlara — <b>yıl basamaklı</b> uygulanır: ilk 12 ay birim, 13-24. aylar
    /// ×(1+oran), 25-36. aylar ×(1+oran)² …
    ///
    /// <para><b>NEDEN ORAN, TUTAR DEĞİL:</b> enflasyon bir orandır; TL tutarı gibi diğer gider
    /// kalemlerinin yanına toplanamaz (0.35 + 12.000 anlamsız bir sayı üretir). Varsayılan 0 →
    /// eskalasyon KAPALI, hesap FAZ-74 öncesiyle aynı.</para>
    /// </summary>
    public decimal EnflasyonOran { get; set; }

    /// <summary>
    /// Finansman yöntemi. <see cref="KrediHesaplamaSekli.Rotatif"/> güvenli-red ile karşılanır
    /// (formül kararı beklemede — KARARLAR.md FAZ-74).
    /// </summary>
    public KrediHesaplamaSekli KrediHesaplamaSekli { get; set; } = KrediHesaplamaSekli.EsitTaksitli;

    /// <summary>
    /// Kaç araçlık teklif. Hesap ARAÇ BAŞINA yapılır; filo toplamları sonuçta AYRI alanlarda
    /// (araç-başı × adet) döner. Girdileri baştan çarpmak yuvarlamayı araç-başı rakamdan koparırdı.
    /// </summary>
    public int AracSayisi { get; set; } = 1;
}

/// <summary>Gider kaleminin dönem içindeki tekrar biçimi.</summary>
public enum MaliyetKalemPeriyot
{
    /// <summary>Yıllık tutar — döneme birim/12 × ay sayısı olarak yayılır.</summary>
    Yillik = 0,
    /// <summary>Aylık tutar — her ay tekrarlar.</summary>
    Aylik = 1,
    /// <summary>Tek seferlik — döneme bir kez girer, enflasyondan etkilenmez.</summary>
    TekSeferlik = 2
}

/// <summary>
/// Tek gider kaleminin dökümü (FAZ-74). <paramref name="DonemTutar"/> <b>satır bazında</b>
/// yuvarlanmıştır; toplam bu satırların toplamıdır ve BİR DAHA yuvarlanmaz (repo kuralı:
/// satır-bazlı yuvarlama, toplamı sonradan yuvarlama yok).
/// </summary>
public sealed record MaliyetGiderKalem(
    string Ad, MaliyetKalemPeriyot Periyot, decimal Birim, decimal DonemTutar);

/// <summary>
/// Maliyet hesabı sonucu (salt-hesap; deftere/bakiyeye yazmaz).
/// Tüm tutarlar <b>araç başına</b>; filo toplamları <see cref="AracSayisi"/> ile türetilir.
/// </summary>
public sealed record MaliyetHesapSonuc(
    decimal ResidualDeger,
    decimal NetAmortisman,
    decimal FinansmanFaiz,
    decimal FinansmanVergi,
    decimal Damga,
    decimal ToplamGider,
    decimal ToplamMaliyet,
    decimal BasaBasAylik,
    decimal Kar,
    decimal TeklifNet,
    decimal TeklifAylikNet,
    decimal TeklifKdvli,
    IReadOnlyList<MaliyetGiderKalem>? GiderKalemleri = null,
    int AracSayisi = 1)
{
    /// <summary>Girilen ama tutarı 0 olan kalemler listede yer almaz (döküm okunur kalsın).</summary>
    public IReadOnlyList<MaliyetGiderKalem> Kalemler => GiderKalemleri ?? [];

    // Filo toplamları TÜRETİLİR (kolon/alan değil): araç-başı × adet. Tam sayı çarpımı olduğu
    // için ek yuvarlama gerekmez — araç-başı rakam zaten 2 hanede kapalı.
    public decimal FiloToplamMaliyet => ToplamMaliyet * AracSayisi;
    public decimal FiloTeklifNet => TeklifNet * AracSayisi;
    public decimal FiloTeklifAylikNet => TeklifAylikNet * AracSayisi;
    public decimal FiloTeklifKdvli => TeklifKdvli * AracSayisi;
}
