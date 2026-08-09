using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç sipariş/tedarik kaydı (roadmap L3): tedarikçiden araç siparişi (FiloStatus.Siparis akışı).
/// Tenant-owned + auditable; full-CRUD (mali değişmez belge DEĞİL). DEFTER POSTLAMAZ — teslim alınınca
/// araç/satınalma faturalama ayrı; bu kayıt sipariş takibinin kaynağıdır.
///
/// <para><b>FAZ-17:</b> cari bağı, dosya/temsilci/spesifikasyon alanları ve üç fiyat katmanı
/// (Piyasa/Ops/Filo) eklendi. Hepsi <b>BİLGİ</b>'dir: "defter postlamaz" ilkesi korundu ve
/// <see cref="BirimFiyat"/> tek "resmi" birim tutar olarak KALDI.</para>
/// </summary>
public class AracSiparis : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (SP-000001).</summary>
    public string No { get; set; } = string.Empty;

    public string Tedarikci { get; set; } = string.Empty;

    /// <summary>
    /// FAZ-17: siparişin tedarikçisi bir <b>Cari</b> kaydıysa o carinin kimliği (canlıdaki
    /// Musteri_No/Ad_Soyad arama-seç alanı). <b>ADDITIVE</b> — serbest metin <see cref="Tedarikci"/>
    /// KALDI ve zorunlu olmayı sürdürüyor; cari yalnız ilişkilendirmedir (eski kayıtlarda cari bağı
    /// gerçekten YOKTUR, NULL "bağlanmamış" demektir).
    /// <para><b>Cari bakiyesine DOKUNMAZ</b> — bu alan deftere hiçbir kayıt yazmaz (KARARLAR.md
    /// genel politikası). Tedarikçiye borç, satın alma faturası/ödeme akışından doğar.</para>
    /// </summary>
    public Guid? TedarikciCariId { get; set; }

    public DateTimeOffset SiparisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? BeklenenTeslim { get; set; }

    /// <summary>FAZ-17: canlıdaki Imza_Tarih — sipariş sözleşmesinin imza günü. Sistemin
    /// <see cref="SiparisTarihi"/>'nden ayrı bir bilgidir (teklif verilen gün ≠ imza günü).</summary>
    public DateTimeOffset? ImzaTarih { get; set; }

    /// <summary>FAZ-17: canlıdaki Dosya_No — tedarikçi/bayi dosya referansı. Boşluksuz
    /// <see cref="No"/> alanının YERİNE geçmez, onun yanında dış referans taşır.</summary>
    public string? DosyaNo { get; set; }

    /// <summary>FAZ-17: canlıdaki Satis_Temsilci — bayinin satış temsilcisi (serbest metin).</summary>
    public string? SatisTemsilci { get; set; }

    /// <summary>FAZ-17: canlıdaki Ozel_Temsilci — özel/filo temsilcisi (serbest metin).</summary>
    public string? OzelTemsilci { get; set; }

    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Grup { get; set; }

    /// <summary>FAZ-17: canlıdaki Versiyon — donanım/versiyon adı (ör. "1.5 Turbo Elite").</summary>
    public string? Versiyon { get; set; }

    /// <summary>FAZ-17: canlıdaki Opsiyon — sipariş opsiyonları/ek donanım listesi (serbest metin).</summary>
    public string? Opsiyon { get; set; }

    /// <summary>FAZ-17: canlıdaki Renk — dış renk.</summary>
    public string? Renk { get; set; }

    /// <summary>FAZ-17: canlıdaki Ic_Renk — iç döşeme rengi.</summary>
    public string? IcRenk { get; set; }

    /// <summary>FAZ-17: canlıdaki Kaynak_Tip (ör. "ÖzMal") — aracın hangi kaynaktan temin edildiği.
    /// Serbest metin: canlıdaki seçenek kümesi doğrulanamadığı için enum'a KİLİTLENMEDİ.</summary>
    public string? KaynakTip { get; set; }

    /// <summary>FAZ-17: canlıdaki Satis_Tipi (ör. "Sıfır" / "2. El").</summary>
    public string? SatisTipi { get; set; }

    /// <summary>FAZ-17: canlıdaki TSBKayit_No — TSB/geçici plaka kayıt referansı.</summary>
    public string? TsbKayitNo { get; set; }

    /// <summary>
    /// FAZ-17: siparişin ilişkilendirildiği araç kredisi (canlıdaki Kredi_No). Composite tenant-FK
    /// ile <see cref="AracKredi"/>'ye bağlanır → başka tenant'ın kredisine bağlanmak yapısal olarak
    /// imkânsız. <b>BİLGİ ALANI</b>: krediye taksit/gider yazmaz, kredinin bakiyesini değiştirmez;
    /// sipariş kaydı defter postlamaz.
    /// </summary>
    public Guid? KrediId { get; set; }

    public int Adet { get; set; } = 1;

    /// <summary>
    /// <b>RESMİ sipariş birim tutarı</b> (canlıdaki Onay_Fiyat/Liste_Fiyat karşılığı). Sipariş
    /// toplamı = <see cref="Adet"/> × BirimFiyat ve tüm ekran/export toplamları BUNU kullanır.
    /// FAZ-17'de eklenen üç fiyat katmanı bu alanın yerine GEÇMEZ (bkz. <see cref="PiyasaFiyat"/>).
    /// </summary>
    public decimal BirimFiyat { get; set; }

    /// <summary>
    /// FAZ-17 — çok katmanlı fiyat: <b>piyasa</b> fiyatı (canlıdaki Piyasa_Fiyat). <b>SALT BİLGİ.</b>
    /// <para>Resmi tutar DEĞİŞMEDİ: tek resmi birim tutar <see cref="BirimFiyat"/>'tır. Bu katman
    /// hiçbir toplama, rapora ya da deftere girmez (KARARLAR.md FAZ-17 kararı: "çok katmanlı fiyat
    /// yalnız alan/altyapı"). <c>null</c> = girilmemiş (0 ile karıştırılmasın: 0 "bedelsiz" demek
    /// olurdu).</para>
    /// </summary>
    public decimal? PiyasaFiyat { get; set; }

    /// <summary>FAZ-17 — çok katmanlı fiyat: <b>operasyon</b> fiyatı (canlıdaki Ops_Fiyat).
    /// SALT BİLGİ — bkz. <see cref="PiyasaFiyat"/>.</summary>
    public decimal? OpsFiyat { get; set; }

    /// <summary>FAZ-17 — çok katmanlı fiyat: <b>filo</b> fiyatı (canlıdaki Filo_Fiyat).
    /// SALT BİLGİ — bkz. <see cref="PiyasaFiyat"/>.</summary>
    public decimal? FiloFiyat { get; set; }

    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public SiparisDurum Durum { get; set; } = SiparisDurum.Bekliyor;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
