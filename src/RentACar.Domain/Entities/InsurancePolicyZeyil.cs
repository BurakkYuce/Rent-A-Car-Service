using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Sigorta poliçesi ZEYLİ (poliçe eki) — referans sistem <c>arac_sigorta_islemleri.aspx</c> zeyil
/// ızgarasının karşılığı (Zeyil No/Tarih/Tanzim/Değer/Brüt/Net/Fon-Vergi/Tipi/Neden).
/// Tenant-owned + auditable; güncellenebilir/silinebilir (mali belge DEĞİL → değişmezlik
/// trigger'ı yok, tam CRUD).
///
/// <para><b>DEFTERE YAZMAZ (kilitli karar — <c>docs/KARARLAR.md</c> "GENEL POLİTİKA —
/// yeni tutar alanları deftere yazmaz").</b> <see cref="Brut"/>/<see cref="Net"/>/
/// <see cref="FonVergi"/>/<see cref="Deger"/> yalnız BİLGİ/GEÇMİŞ alanlarıdır; hiçbir
/// <c>AccountLedgerEntry</c> üretmezler, cari bakiyeye ve gelir-gider raporlarına GİRMEZLER.
/// Gerekçe: gerçek para hareketi Kasa/Banka tahsilat-ödeme akışından geçer; ikinci bir yol
/// açmak çift-sayım üretir. Zeyil primi tahsil edildiğinde normal ödeme akışı kullanılır.
/// Bu kural <c>SigortaZeyilTests.Zeyil_deftere_yazmaz_*</c> regresyon testleriyle kilitlidir.</para>
/// </summary>
public class InsurancePolicyZeyil : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Bağlı olduğu poliçe (composite tenant-FK → InsurancePolicy).</summary>
    public Guid PolicyId { get; set; }

    /// <summary>Zeyil numarası (poliçe içinde tekil — sigortacının verdiği ek no).</summary>
    public string ZeyilNo { get; set; } = string.Empty;

    /// <summary>Zeyil (poliçe ekinin yürürlük) tarihi.</summary>
    public DateTimeOffset Tarih { get; set; }

    /// <summary>Tanzim (düzenlenme) tarihi — zeyil tarihinden farklı olabilir.</summary>
    public DateTimeOffset? Tanzim { get; set; }

    /// <summary>Zeyille değişen sigorta değeri (araç/teminat bedeli). Negatif olamaz.</summary>
    public decimal Deger { get; set; }

    /// <summary>
    /// Brüt prim farkı. NEGATİF OLABİLİR: "tenzil/iade zeyli" primi düşürür (poliçe iptali,
    /// teminat daralması). Bilgi alanı olduğu için işaret serbest bırakıldı — bir hesaba
    /// girmediğinden yön hatası üretemez.
    /// </summary>
    public decimal Brut { get; set; }

    /// <summary>Net prim farkı (brütten fon/vergi düşülmüş). Negatif olabilir (bkz. <see cref="Brut"/>).</summary>
    public decimal Net { get; set; }

    /// <summary>Fon + vergi payı. Negatif olabilir (bkz. <see cref="Brut"/>).</summary>
    public decimal FonVergi { get; set; }

    /// <summary>Zeyil tipi (Zam/Tenzil/Değişiklik… — serbest metin, canlıda da sabit liste değil).</summary>
    public string? Tipi { get; set; }

    /// <summary>Zeyil nedeni (serbest metin).</summary>
    public string? Neden { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
