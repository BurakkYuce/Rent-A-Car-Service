using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Marka-özel düzenlenebilir belge şablonu (PDF metin bölümleri). Firma başlığı/logo zaten
/// TenantSettings'ten gelir; bu tablo, o ana kadar KODA GÖMÜLÜ olan metin bloklarını (belge başlığı,
/// iki dilli hukuki metin, ek koşullar, alt bilgi) tenant başına düzenlenebilir yapar. Tenant her
/// belge türü için birden çok İSİMLİ şablon (ör. "Kurumsal"/"Bireysel") tanımlar; kira formunda
/// hangisinin basılacağı seçilir (RentalContract.BelgeSablonId). Seçim yoksa o belge türünün
/// VarsayilanMi=true şablonu; o da yoksa koddaki sabit (BelgeSablonVarsayilan) basılır.
/// Bölüm metinleri null → o bölümde varsayılan basılır (kısmi override serbest). Defter postalamaz.
/// </summary>
public class BelgeSablon : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Şablonun uygulandığı belge türü (kira sözleşmesi / fatura / makbuz).</summary>
    public BelgeTuru BelgeTuru { get; set; }

    /// <summary>Şablon adı (ör. "Kurumsal", "Bireysel"); tür içinde benzersiz.</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>Bu belge türü için varsayılan şablon mu (seçim yoksa basılır; tür başına en çok bir tane).</summary>
    public bool VarsayilanMi { get; set; }

    public bool Aktif { get; set; } = true;

    // ---- Bölüm metinleri (null → o bölümde koddaki varsayılan basılır) ----
    /// <summary>Belge başlığı override (ör. sözleşmede "ARAÇ TESLİM BELGESİ / RENTAL AGREEMENT").</summary>
    public string? BelgeBasligi { get; set; }
    /// <summary>Sözleşme sol hukuki metin bloğu (yalnız KiraSozlesmesi).</summary>
    public string? HukukiMetinSol { get; set; }
    /// <summary>Sözleşme sağ hukuki metin bloğu (yalnız KiraSozlesmesi).</summary>
    public string? HukukiMetinSag { get; set; }
    /// <summary>Ek koşullar varsayılanı (kira-özel EkKosullar boşsa sözleşmeye bu basılır).</summary>
    public string? EkKosullarVarsayilan { get; set; }
    /// <summary>Belge alt bilgisi / footer (token'lı; ör. "{FirmaMarka} — {BelgeNo}").</summary>
    public string? AltBilgi { get; set; }

    /// <summary>
    /// Sözleşme PDF'inde FİZİKSEL İMZA alanları basılsın mı. Varsayılan <c>true</c> — mevcut
    /// davranış birebir korunur. <c>false</c>: elektronik onaylı sözleşme senaryosu; imza satırları
    /// atlanır. Kredi kartı bilgi bloğu bundan ETKİLENMEZ (o ayrı bir alan, PCI gereği fizikî alınır).
    /// </summary>
    public bool ImzaAlaniGoster { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
