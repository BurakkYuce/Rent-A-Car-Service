using System.Security.Cryptography;
using System.Text;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Security;

/// <summary>
/// IPiiHasher: HMAC-SHA256 blind index (KVKK/F2). Anahtar konfigürasyondan (Pii:HmacKey →
/// AddInfrastructure parametresi) gelir; verilmezse SABİT geliştirme anahtarı kullanılır —
/// üretimde Web/Api Program.cs anahtar yoksa AÇILMAYI REDDEDER (oradaki guard).
/// Anahtar değişirse mevcut özetler eşleşmez olur → anahtar kalıcı ve yedeklenmelidir
/// (bkz. docs/ops/yedekleme.md — DataProtection anahtarlarıyla aynı kapsamda).
/// </summary>
public sealed class HmacPiiHasher(string? hmacKey) : IPiiHasher
{
    public const string DevelopmentKey = "dev-only-pii-hmac-key-uretimde-kullanma";

    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        string.IsNullOrWhiteSpace(hmacKey) ? DevelopmentKey : hmacKey);

    public string? Hash(Guid tenantId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Tenant tuzu: "{tenantN}:{değer}" — cross-tenant hash eşitliği (korelasyon) imkânsız.
        var bytes = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{tenantId:N}:{value.Trim()}"));
        return Convert.ToHexStringLower(bytes);
    }
}
