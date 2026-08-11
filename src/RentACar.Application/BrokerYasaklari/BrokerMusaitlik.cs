using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.BrokerYasaklari;

/// <summary>
/// FAZ-73 — broker müsaitlik çiti (canlı <c>broker_musaitlik_listesi.aspx</c>). Bir broker/kanal
/// (rez kaynağı) seçildiğinde, o brokera KAPALI araç gruplarının müsaitlik listesinde
/// GÖSTERİLMEMESİNİ sağlar.
///
/// <para><b>SAF fonksiyon — para taşımaz.</b> Fiyat hesabına HİÇ dokunmaz: yalnız hangi satırın
/// listede görüneceğine karar verir. Fiyat, listede kalan gruplar için değişmeden
/// <c>RentalQuoteEngine</c>'den okunur (FAZ-73 PARA disiplini: yeni yüzey fiyatı kendi hesaplamaz).</para>
///
/// <para><b>Kaynak seçilmemişse çit KAPALIDIR</b> (hiçbir satır elenmez): "broker belirtilmedi"
/// ile "her brokera kapalı" aynı şey değildir; genel müsaitlik araması FAZ-48'deki davranışını
/// birebir korur.</para>
///
/// <para>Bu tip, bugüne dek çağıranı olmayan <see cref="BrokerYasakService.KapsarMi"/> CSV okuma
/// sözleşmesinin İLK üretim tüketicisidir (biçim yeniden yorumlanmadı).</para>
/// </summary>
public static class BrokerMusaitlik
{
    /// <summary>
    /// Verilen kapsam için engel oluşturan İLK yasağı döndürür; engel yoksa <c>null</c>.
    ///
    /// <para>Eşleşme kuralları — kapsam alanı boşsa o boyut "tümü" sayılır (yasağın kendi
    /// tanımındaki konvansiyon), doluysa değer birebir kapsamda olmalıdır. Tarih penceresi kira
    /// BAŞLANGIÇ tarihine göre okunur (fiyat motorunun geçerlilik konvansiyonuyla aynı yön).</para>
    ///
    /// <para>Sıralama <c>Kod</c> ile deterministiktir — aynı girdi hep aynı gerekçeyi gösterir.</para>
    /// </summary>
    /// <param name="aktifYasaklar">Yalnız aktif yasaklar (çağıran <c>ListActiveAsync</c> ile getirir).</param>
    /// <param name="kaynak">Seçili rez kaynağı/broker adı; boş → çit uygulanmaz (null döner).</param>
    /// <param name="aracGrupKod">Değerlendirilen araç grubu kodu.</param>
    /// <param name="bolge">Şube/bölge adı (boş geçilebilir).</param>
    /// <param name="gun">Kira gün sayısı — <c>MinGun</c> kısıtı bunun ALTINDA yasaklar.</param>
    /// <param name="basTar">Kira başlangıcı (yasak geçerlilik penceresi bununla karşılaştırılır).</param>
    public static BrokerYasak? Engel(
        IReadOnlyList<BrokerYasak> aktifYasaklar, string? kaynak, string? aracGrupKod,
        string? bolge, int gun, DateTimeOffset basTar)
    {
        if (string.IsNullOrWhiteSpace(kaynak)) return null;
        if (aktifYasaklar.Count == 0) return null;

        return aktifYasaklar
            .Where(y => Kapsar(y, kaynak, aracGrupKod, bolge, basTar) && Kisitliyor(y, gun))
            .OrderBy(y => y.Kod, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool Kapsar(BrokerYasak y, string kaynak, string? aracGrupKod, string? bolge, DateTimeOffset basTar)
    {
        if (y.GecerlilikBas is { } b && b > basTar) return false;
        if (y.GecerlilikBit is { } t && t < basTar) return false;

        // Kaynak: yasakta boşsa tüm kaynaklar; doluysa birebir (Türkçe harf duyarsız — "İnternet"
        // ile "internet" aynı kanaldır ve OrdinalIgnoreCase bunu göremez).
        if (!string.IsNullOrWhiteSpace(y.Kaynak)
            && !TurkishText.EqualsIgnoreTurkishCase(y.Kaynak.Trim(), kaynak.Trim())) return false;

        // Grup/bölge: CSV çoklu kapsam (FAZ-70 biçimi). Kapsam doluyken değer boşsa eşleşme YOK —
        // bilinmeyen bir grubu yasak kapsamında saymak, listeden sessizce araç silerdi.
        if (!string.IsNullOrWhiteSpace(y.AracGrupKod)
            && !BrokerYasakService.KapsarMi(y.AracGrupKod, aracGrupKod)) return false;
        if (!string.IsNullOrWhiteSpace(y.Bolge)
            && !BrokerYasakService.KapsarMi(y.Bolge, bolge)) return false;

        return true;
    }

    /// <summary>Kapsamdaki kısıt bu kira süresini gerçekten engelliyor mu.</summary>
    private static bool Kisitliyor(BrokerYasak y, int gun)
        => y.TumSatisKapali || (y.MinGun is { } mg && gun < mg);
}
