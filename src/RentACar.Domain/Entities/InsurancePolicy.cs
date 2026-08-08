using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç sigorta poliçesi (Trafik/Kasko). Tenant-owned + auditable. Güncellenebilir kayıt
/// (mali belge değil → değişmezlik trigger'ı yok). Bitiş tarihi vade panosunu besler.
/// </summary>
public class InsurancePolicy : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid VehicleId { get; set; }
    public InsuranceType Tip { get; set; } = InsuranceType.Trafik;

    public string? PoliceNo { get; set; }
    public DateTimeOffset Baslangic { get; set; }
    public DateTimeOffset Bitis { get; set; }
    public string? Firma { get; set; }
    public string? Acenta { get; set; }

    public decimal Prim { get; set; }
    public string Currency { get; set; } = "TRY";

    /// <summary>Zeyil/ek prim (roadmap J3): ödemede prime eklenir.</summary>
    public decimal ZeyilPrim { get; set; }
    /// <summary>Ödendi mi (roadmap J3): true → defter kaydı yazılmış, tekrar ödenemez.</summary>
    public bool Odendi { get; set; }

    // ---- FAZ-15: sigorta değer tabanı (arac_sigorta_islemleri.aspx paritesi) ----
    // BİLGİ ALANLARI — hiçbir hesaba/deftere girmezler (KARARLAR.md genel politikası). Teminat
    // tabanının ekranda görünmesi içindir; hasar/rücu tavanı gibi bir hesaplamaya BAĞLANMAZ.

    /// <summary>Poliçedeki araç (kasko) değeri. Bilgi alanı.</summary>
    public decimal? AracDegeri { get; set; }
    /// <summary>İhtiyari Mali Mesuliyet (İMM) teminat değeri. Bilgi alanı.</summary>
    public decimal? ImmDegeri { get; set; }
    /// <summary>Aksesuar teminat değeri. Bilgi alanı.</summary>
    public decimal? AksesuarDegeri { get; set; }

    /// <summary>
    /// Poliçenin ödenmemiş bakiyesi — <b>BİLGİ ALANI, DEFTERE YAZMAZ</b>. Kayıt açılırken
    /// <c>= Prim</c>, poliçe ödendiğinde (<c>SigortaOdeAsync</c>, defter kaydıyla AYNI
    /// transaction) 0'a düşer.
    ///
    /// <para>ZEYİL EKLEMEK BU ALANI DEĞİŞTİRMEZ: zeyil primi de bilgi alanıdır ve tahsilatı
    /// Kasa/Banka akışından geçer. Kalan'ı zeyille büyütmek onu "ödenmemiş borç" göstergesine
    /// çevirir, yani defterin dışında ikinci bir borç kaynağı açardı (çift-sayım).</para>
    /// </summary>
    public decimal Kalan { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
