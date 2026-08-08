using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Lokasyon-şube drop matrisi (roadmap N2): bir lokasyon × şube ikilisi için karşılama/çalışma şekli +
/// özel iletişim. Aracı bir lokasyonda alıp başka şubeye bırakma (drop) konfigürasyonu. Tenant-owned +
/// auditable; full-CRUD basit master. Benzersiz: (TenantId, Lokasyon, Sube).
///
/// <para><b>ANLAM NETLEŞTİRMESİ (FAZ-22):</b> <see cref="Lokasyon"/> aracın BIRAKILDIĞI —
/// yani DÖNÜŞ — lokasyonudur; <see cref="Sube"/> ÇIKIŞ şubesidir. Motorda kanıtlandı:
/// <c>FeeLineService.DropUcretCozAsync</c> <c>Lokasyon</c>'u kiranın <c>DonusOfisi</c>'yle,
/// <c>Sube</c>'yi <c>CikisOfisi</c>'yle eşleştirir. Faz planı bu alanın "çıkış lokasyonu" olduğunu
/// varsayıyordu — TERSİ. Kolon adı geriye uyum için korundu, arayüz etiketi "Dönüş Lokasyonu"
/// yapıldı ve çıkış tarafı için AYRI bir alan eklendi.</para>
/// </summary>
public class DropTanim : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>DÖNÜŞ lokasyonu — aracın bırakıldığı ofis (kiranın DonusOfisi ile eşleşir).</summary>
    public string Lokasyon { get; set; } = string.Empty;

    /// <summary>ÇIKIŞ şubesi — aracın alındığı şube (kiranın CikisOfisi ile eşleşir).</summary>
    public string Sube { get; set; } = string.Empty;
    public string? KarsilamaSekli { get; set; }
    public string? CalismaSekli { get; set; }
    public string? OzelIletisim { get; set; }

    /// <summary>Drop ücreti (FAZ 3.A3b) — NET ("KDV hariç", tek seferlik). Kira DonusOfisi bu satırın
    /// Lokasyon'una eşit ve çıkış≠dönüş ofis ise SYS-DROP sistem satırı üretir; null/0 = ücret yok.
    /// Aynı lokasyona birden çok satırda çıkış-şubesi eşleşen satır tercih edilir (deterministik).</summary>
    public decimal? Ucret { get; set; }

    // ---- FAZ-22 derinlik (additive, nullable — mevcut satırların davranışı DEĞİŞMEZ) ----

    /// <summary>
    /// ÇIKIŞ LOKASYONU daraltması (opsiyonel). <see cref="Sube"/> şube düzeyinde eşleşir; bu alan
    /// dolduruldu ise kural YALNIZ o çıkış ofisinde geçerlidir. Null = eski davranış (şube yeter).
    /// Daha ÖZGÜL satır (dolu CikisLokasyon) daha genel olanı yener.
    /// </summary>
    public string? CikisLokasyon { get; set; }

    /// <summary>
    /// Kuralın geçerli olması için gereken ASGARİ kira günü (opsiyonel). Kira bundan kısaysa bu
    /// satır uygulanmaz. Null = gün koşulu yok (eski davranış).
    /// </summary>
    public int? MinGun { get; set; }

    /// <summary>Karşılama/hazırlık süresi (dakika) — BİLGİ alanı, ücrete girmez.</summary>
    public int? ManSuresi { get; set; }

    /// <summary>
    /// İkinci drop ücret kalemi — <b>BİLGİ AMAÇLI; hesaba GİRMEZ.</b> Canlıda karşılığı var ama
    /// formülü kuruş bazında kalibre edilmedi; uydurulmuş bir para kuralı eklemektense alan
    /// taşınabilir tutulup motorun dışında bırakıldı (ekranda da böyle etiketli).
    /// </summary>
    public decimal? Drop2 { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
