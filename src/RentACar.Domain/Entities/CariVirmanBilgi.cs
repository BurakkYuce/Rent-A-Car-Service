using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Cari↔cari virmanın DEFTER DIŞI künye bilgisi (canlı <c>cari_virman.aspx</c> alanları).
///
/// <para><b>Neden ayrı tablo:</b> tutar/tarih/yön zaten <see cref="AccountLedgerEntry"/>'de ve orası
/// değişmez bir mali belge. Vade/makbuz no/şube gibi operasyonel künyeyi ya defterin
/// <c>Description</c> metnine gömmek (sonradan ayrıştırılamaz, kırılgan) ya da çekirdek defter
/// tablosuna tek bir işlem türü için kolon eklemek gerekirdi. İkisi de yanlış; künye kendi
/// tablosunda durur.</para>
///
/// <para><b>PARA TAŞIMAZ.</b> Tutar/döviz/kur burada BİLİNÇLİ olarak yok — çift kaynak olurdu.
/// Liste ekranı tutarı her zaman defterden okur.</para>
///
/// <para><see cref="Id"/> defterdeki <c>SourceId</c>'nin AYNISIDIR: virmanın iki bacağı da o
/// kimliği taşır, künye de aynı kimlikle eşleşir. Böylece idempotency tek anahtardan yürür —
/// çift-submit'te defter satırları da künye de aynı unique kısıta çarpar.</para>
/// </summary>
public class CariVirmanBilgi : ITenantOwned, IAuditable
{
    /// <summary>Defterdeki <c>SourceId</c> ile AYNI değer (virman kimliği).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Bakiyesi AZALAN cari (defterde Credit bacağı).</summary>
    public Guid KaynakCariId { get; set; }
    /// <summary>Bakiyesi ARTAN cari (defterde Debit bacağı).</summary>
    public Guid HedefCariId { get; set; }

    /// <summary>Virman tarihi — defterdeki <c>EntryDateUtc</c> ile aynı (listeleme/sıralama için).</summary>
    public DateTimeOffset Tarih { get; set; }

    /// <summary>Vade tarihi (bilgi amaçlı; tahsilat akışına bağlı değil).</summary>
    public DateTimeOffset? Vade { get; set; }
    public string? MakbuzNo { get; set; }
    /// <summary>İşlemin yapıldığı şube (serbest metin).</summary>
    public string? Sube { get; set; }
    /// <summary>İşlemi yapan kullanıcı adı — oturumdan alınır, formdan DEĞİL.</summary>
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
