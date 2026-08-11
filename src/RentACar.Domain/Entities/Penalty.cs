using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Trafik cezası. Tenant-owned + auditable. Tebliğ tarihinden vade hesaplanır. Müşteriye
/// yansıtılınca (Yansitildi) cari borçlanır (Borç Cari / Alacak Gelir) — yansıtma kaydı
/// immutable. Ceza başlığı güncellenebilir (durum/ödeme), ama yansıtma defteri değişmez.
/// </summary>
public class Penalty : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (CZ-000001).</summary>
    public string No { get; set; } = string.Empty;

    public string CezaTuru { get; set; } = string.Empty;
    public DateTimeOffset TebligTarihi { get; set; }
    public DateTimeOffset VadeTarihi { get; set; }

    public Guid? VehicleId { get; set; }
    public Guid? CariId { get; set; }       // yansıtılacak müşteri
    public Guid? RentalId { get; set; }      // ilgili kira sözleşmesi

    /// <summary>
    /// Ceza toplamı. FAZ-60'tan itibaren <b>türetilmiş</b>tir: <c>Σ PenaltySatir.Tutar</c>.
    /// Satır verilmeden kayıt açılırsa tek satır maddeleştirilir, değişmez yine korunur.
    /// </summary>
    public decimal Tutar { get; set; }
    /// <summary>Başlık sebebi (tek satırlı cezada satırın sebebiyle aynı; çok satırlıda özet).</summary>
    public string? Sebep { get; set; }

    public CezaDurum Durum { get; set; } = CezaDurum.Yeni;

    // ---- FAZ-60 kısmi ödeme (SATIR BAZINDA — KARARLAR.md) ----

    /// <summary>
    /// Ödenen toplam = <c>Σ PenaltySatir.Odenen</c> önbelleği. Ödeme yazan transaction içinde,
    /// <c>pg_advisory_xact_lock("ceza:{tenant}:{cezaId}")</c> arkasında satırlardan yeniden
    /// hesaplanır — başlık ile satırlar ayrışamaz.
    /// </summary>
    public decimal OdenenTutar { get; set; }

    /// <summary>Kalan toplam = <c>Tutar − OdenenTutar</c>. ASLA negatif olamaz (DB CHECK).</summary>
    public decimal Kalan { get; set; }

    /// <summary>Son ödemenin tarihi (canlı <c>Odenme_Tarih</c>). İlk ödemede dolar, her ödemede güncellenir.</summary>
    public DateTimeOffset? OdenmeTarihi { get; set; }

    // ---- FAZ-60 bilgi alanları (canlı cezalar.aspx paritesi; deftere GİRMEZ) ----

    /// <summary>Ceza saati "HH:mm" (canlı <c>Ceza_Saat</c> — serbest metin, tarihten ayrı).</summary>
    public string? Saat { get; set; }
    /// <summary>Ceza yeri (canlı <c>Ceza_Yeri</c>).</summary>
    public string? Yer { get; set; }
    /// <summary>İhbarname cep telefonu (canlı <c>Cep_Tel</c>).</summary>
    public string? CepTel { get; set; }
    /// <summary>Makbuz/tebligat no (canlı <c>Makbuz_No</c>) — arama filtresi.</summary>
    public string? MakbuzNo { get; set; }
    /// <summary>
    /// İşlemi yapan şube (canlı <c>Islem_Sube</c>). SERBEST METİN — şube kapsamı/yetki
    /// kararına GİRMEZ (bilinçli: cezanın kapsamı aracın/kiranın şubesidir). Şube birleştirmede
    /// yeniden adlandırılır (BranchRepository kapsamı).
    /// </summary>
    public string? IslemSube { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
