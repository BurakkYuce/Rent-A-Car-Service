using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Security;

/// <summary>
/// <see cref="ISecretProtector"/>'ın DataProtection uygulaması (roadmap D1). Sabit purpose ile bir
/// IDataProtector türetir; anahtar key-ring'den gelir (DI'da KALICI: PersistKeysToFileSystem +
/// SetApplicationName → restart/redeploy sonrası aynı anahtarla çözülür). Singleton.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("RentACar.TenantSecrets.v1");

    public string? Protect(string? plaintext)
        => string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch (CryptographicException) { return null; } // anahtar değişti/bozuk → çökme yerine null
    }
}
