using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tenant ayarları (roadmap D1; canlı "ayarlar" karşılığı): firma bilgisi + entegrasyon kimlik slotları.
/// Tenant başına TEK satır (TenantId unique). Hassas alanlar (*Enc) at-rest ŞİFRELİ saklanır
/// (servis ISecretProtector ile yazar/okur); kullanıcı adı/başlık/merchant gibi gizli-olmayanlar düz metin.
/// Entegrasyonların (e-Fatura/SMS/POS) ön koşulu — değerler kimliksiz boş kurulur, sonra doldurulur.
/// </summary>
public class TenantSettings : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    // Firma (düz metin)
    public string? FirmaUnvan { get; set; }
    public string? FirmaVergiDairesi { get; set; }
    public string? FirmaVergiNo { get; set; }
    public string? FirmaAdres { get; set; }
    public string? FirmaTel { get; set; }
    public string? FirmaEmail { get; set; }

    // Entegrasyon kimlikleri — gizli-olmayan düz metin; sır (*Enc) ŞİFRELİ cipher.
    public string? EFaturaKullanici { get; set; }
    public string? EFaturaSifreEnc { get; set; }
    public string? SmsBaslik { get; set; }
    public string? SmsApiKeyEnc { get; set; }
    public string? PosMerchantId { get; set; }
    public string? PosApiKeyEnc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
