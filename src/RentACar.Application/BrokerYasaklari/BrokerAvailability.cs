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
/// <para>Bu tip, bugüne dek çağıranı olmayan <see cref="BrokerBanService.IsCovered"/> CSV okuma
/// sözleşmesinin İLK üretim tüketicisidir (biçim yeniden yorumlanmadı).</para>
/// </summary>
public static class BrokerAvailability
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
    /// <param name="activeBans">Yalnız aktif yasaklar (çağıran <c>ListActiveAsync</c> ile getirir).</param>
    /// <param name="source">Seçili rez kaynağı/broker adı; boş → çit uygulanmaz (null döner).</param>
    /// <param name="vehicleGroupCode">Değerlendirilen araç grubu kodu.</param>
    /// <param name="region">Şube/bölge adı (boş geçilebilir).</param>
    /// <param name="day">Kira gün sayısı — <c>MinGun</c> kısıtı bunun ALTINDA yasaklar.</param>
    /// <param name="startDate">Kira başlangıcı (yasak geçerlilik penceresi bununla karşılaştırılır).</param>
    public static BrokerYasak? Block(
        IReadOnlyList<BrokerYasak> activeBans, string? source, string? vehicleGroupCode,
        string? region, int day, DateTimeOffset startDate)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (activeBans.Count == 0) return null;

        return activeBans
            .Where(y => Covers(y, source, vehicleGroupCode, region, startDate) && Restricts(y, day))
            .OrderBy(y => y.Kod, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool Covers(BrokerYasak y, string source, string? vehicleGroupCode, string? region, DateTimeOffset startDate)
    {
        if (y.GecerlilikBas is { } b && b > startDate) return false;
        if (y.GecerlilikBit is { } t && t < startDate) return false;

        // Kaynak: yasakta boşsa tüm kaynaklar; doluysa birebir (Türkçe harf duyarsız — "İnternet"
        // ile "internet" aynı kanaldır ve OrdinalIgnoreCase bunu göremez).
        if (!string.IsNullOrWhiteSpace(y.Kaynak)
            && !TurkishText.EqualsIgnoreTurkishCase(y.Kaynak.Trim(), source.Trim())) return false;

        // Grup/bölge: CSV çoklu kapsam (FAZ-70 biçimi). Kapsam doluyken değer boşsa eşleşme YOK —
        // bilinmeyen bir grubu yasak kapsamında saymak, listeden sessizce araç silerdi.
        if (!string.IsNullOrWhiteSpace(y.AracGrupKod)
            && !BrokerBanService.IsCovered(y.AracGrupKod, vehicleGroupCode)) return false;
        if (!string.IsNullOrWhiteSpace(y.Bolge)
            && !BrokerBanService.IsCovered(y.Bolge, region)) return false;

        return true;
    }

    /// <summary>Kapsamdaki kısıt bu kira süresini gerçekten engelliyor mu.</summary>
    private static bool Restricts(BrokerYasak y, int day)
        => y.TumSatisKapali || (y.MinGun is { } mg && day < mg);
}
