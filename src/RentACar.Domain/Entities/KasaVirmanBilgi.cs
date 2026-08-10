using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// FAZ-50 — Kasa/Banka virmanının DEFTER DIŞI künye bilgisi (canlı <c>kasa_virman.aspx</c> /
/// <c>banka_virman.aspx</c> alanları). <see cref="CariVirmanBilgi"/> ile aynı desen.
///
/// <para><b>Neden ayrı tablo:</b> tutar/tarih/yön zaten <see cref="AccountLedgerEntry"/>'de ve orası
/// değişmez bir mali belge. Makbuz no/şube gibi operasyonel künyeyi defterin <c>Description</c>
/// metnine gömmek (sonradan ayrıştırılamaz) ya da çekirdek defter tablosuna tek işlem türü için
/// kolon eklemek gerekirdi; ikisi de yanlış.</para>
///
/// <para><b>PARA TAŞIMAZ.</b> Tutar/döviz/kur burada BİLİNÇLİ olarak yok — çift kaynak olurdu.
/// Liste ekranı tutarı her zaman defterden okur.</para>
///
/// <para><see cref="Id"/> defterdeki <c>SourceId</c>'nin AYNISIDIR: virmanın iki bacağı da o
/// kimliği taşır. Böylece idempotency tek anahtardan yürür — çift-submit'te defter satırları da
/// künye de aynı kimliğe çarpar.</para>
/// </summary>
public class KasaVirmanBilgi : ITenantOwned, IAuditable
{
    /// <summary>Defterdeki <c>SourceId</c> ile AYNI değer (virman kimliği).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Bakiyesi AZALAN taraf (defterde Credit bacağı) — Kasa ya da Banka.</summary>
    public LedgerAccountType KaynakTur { get; set; } = LedgerAccountType.Kasa;
    /// <summary>Bakiyesi ARTAN taraf (defterde Debit bacağı) — Kasa ya da Banka.</summary>
    public LedgerAccountType HedefTur { get; set; } = LedgerAccountType.Banka;

    /// <summary>Kaynak spesifik hesap (<c>FinancialAccount</c>); null = hesap belirtilmemiş (legacy kova).</summary>
    public Guid? KaynakHesapId { get; set; }
    /// <summary>Hedef spesifik hesap; null = hesap belirtilmemiş.</summary>
    public Guid? HedefHesapId { get; set; }

    /// <summary>Virman tarihi — defterdeki <c>EntryDateUtc</c> ile aynı (listeleme/sıralama için).</summary>
    public DateTimeOffset Tarih { get; set; }

    public string? MakbuzNo { get; set; }
    /// <summary>İşlemin yapıldığı şube (serbest metin).</summary>
    public string? Sube { get; set; }
    /// <summary>İşlemi yapan kullanıcı adı — oturumdan alınır, formdan DEĞİL.</summary>
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
