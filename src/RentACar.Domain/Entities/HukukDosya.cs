using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Hukuk dosyası (roadmap C2; canlı "hukuk" karşılığı). Master kayıt; doğal anahtar = DosyaNo.
/// Dava/icra takibi (opsiyonel cari ilişkisi).
///
/// <para><b>PARA ÇİTİ (KARARLAR.md "yeni tutar alanları deftere yazmaz" genel politikası):</b>
/// <see cref="Tutar"/> ve <see cref="Tahsilat"/> BİLGİ ALANIDIR — bu kayıt hiçbir
/// <c>AccountLedgerEntry</c> yazmaz, cari bakiyeyi ve raporları DEĞİŞTİRMEZ. Gerçek para hareketi
/// Kasa/Banka tahsilat-ödeme akışından geçer. İki yolu birden açmak çift-sayım üretirdi.</para>
/// </summary>
public class HukukDosya : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string DosyaNo { get; set; } = string.Empty;
    public Guid? CariId { get; set; }
    public HukukTuru Tur { get; set; } = HukukTuru.Dava;
    public string? Avukat { get; set; }
    public decimal Tutar { get; set; }
    public HukukDurum Durum { get; set; } = HukukDurum.Acik;
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;

    // ---- FAZ-41: canlı hukuk_birimi.aspx alan derinliği (additive, hepsi NULLABLE) ----

    /// <summary>Dosyaya konu fatura no — canlıda serbest metin (fatura tablosuna FK DEĞİL; icraya
    /// giden belge harici sistemden gelmiş olabilir). Yalnız arama/eşleştirme amaçlı.</summary>
    public string? FaturaNoTemp { get; set; }

    /// <summary>1. avukatın telefonu.</summary>
    public string? AvukatTel { get; set; }
    /// <summary>1. avukatın e-postası.</summary>
    public string? AvukatMail { get; set; }
    /// <summary>2. avukat adı (canlıda dosya başına iki avukat tutulabiliyor).</summary>
    public string? Avukat2Ad { get; set; }
    public string? Avukat2Tel { get; set; }
    public string? Avukat2Mail { get; set; }

    /// <summary>
    /// Dosyadan bugüne kadar tahsil edildiği BİLDİRİLEN tutar.
    ///
    /// <para><b>BİLGİ AMAÇLI — deftere/cari bakiyeye YAZMAZ.</b> <see cref="Tutar"/> gibi
    /// muhasebeleşmez: bu alanı doldurmak hiçbir <c>AccountLedgerEntry</c> üretmez, cari bakiyeyi
    /// ve gelir/gider raporlarını değiştirmez. Gerçek tahsilat cari üzerinden Kasa/Banka ekranından
    /// girilir ve defteri orada yazar. (KARARLAR.md genel politikası; kırılgan regresyon testiyle
    /// kilitli — <c>HukukTests.Tahsilat_deftere_yazmaz</c>.)</para>
    ///
    /// <para><c>null</c> = "girilmemiş" (0 tahsilatla aynı anlama gelir; ayrımı korumak için kolon
    /// nullable bırakıldı — mevcut satırlara 0 basıp "hiç tahsilat yok" iddiası üretmemek için).</para>
    /// </summary>
    public decimal? Tahsilat { get; set; }

    /// <summary>
    /// Kalan = <see cref="Tutar"/> − <see cref="Tahsilat"/> (kolon DEĞİL, türetilmiş).
    /// Tahsilat tutarı aşarsa negatif olur — fazla tahsilat meşrudur (faiz/masraf), bilinçli olarak
    /// sıfıra kırpılmaz. Formül TEK yerde yaşasın diye ekran/rapor/export hep buradan okur.
    /// </summary>
    public decimal Kalan => Tutar - (Tahsilat ?? 0m);

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
