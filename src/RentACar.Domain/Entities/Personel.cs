using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Personel (roadmap C1; canlı "personel" karşılığı). Master kayıt; doğal anahtar = Kod (sicil).
/// PII alanları (<see cref="TcKimlikEnc"/>, <see cref="MaasEnc"/>) at-rest ŞİFRELİ cipher saklanır
/// (servis ISecretProtector ile yazar/okur) — D1 deseni. TcKimlik benzersizliği YOK (cipher non-deterministik).
/// Sube serbest metin (Branch master ile additive tutarlı).
/// </summary>
public class Personel : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string? TcKimlikEnc { get; set; }   // PII — şifreli
    public DateTimeOffset? IseGiris { get; set; }
    public DateTimeOffset? IseCikis { get; set; }
    public string? SurucuBelgeNo { get; set; }
    public string? MaasEnc { get; set; }       // PII — şifreli
    public string? Sube { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
