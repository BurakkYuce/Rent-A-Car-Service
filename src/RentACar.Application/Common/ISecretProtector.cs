namespace RentACar.Application.Common;

/// <summary>
/// Hassas alanları (tenant entegrasyon kimlikleri, PII vb.) at-rest şifreler (roadmap D1). Anahtar
/// yönetimi altyapıda (DataProtection, KALICI key-ring → restart-güvenli). Null/boş → null (no-op).
/// Cipher çözülemezse (anahtar değişti/bozuk) Unprotect null döner — uygulama çökmez.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Düz metni şifreler; null/boş → null.</summary>
    string? Protect(string? plaintext);

    /// <summary>Cipher'ı çözer; null/boş → null; çözülemezse null.</summary>
    string? Unprotect(string? cipher);
}
