using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Security;

/// <summary>
/// <see cref="ISecretProtector"/>'ın DataProtection uygulaması (roadmap D1). Sabit purpose ile bir
/// IDataProtector türetir; anahtar key-ring'den gelir (DI'da KALICI: PersistKeysToFileSystem +
/// SetApplicationName → restart/redeploy sonrası aynı anahtarla çözülür). Singleton.
/// Web + Api AYNI key-ring'i paylaşmalıdır (RACAR_DP_KEYS) — aksi halde biri diğerinin cipher'ını
/// çözemez; bu durum sessiz kalmasın diye çözme hatası WARNING loglanır (adversarial HIGH-2).
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionSecretProtector>? _log;

    public DataProtectionSecretProtector(
        IDataProtectionProvider provider, ILogger<DataProtectionSecretProtector>? log = null)
    {
        _protector = provider.CreateProtector("RentACar.TenantSecrets.v1");
        _log = log;
    }

    public string? Protect(string? plaintext)
        => string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch (CryptographicException ex)
        {
            // Anahtar değişti/uyumsuz/bozuk → çökme yerine null; ama SESSİZCE değil:
            // Web/Api ayrık key-ring'i (RACAR_DP_KEYS paylaşılmamış) burada görünür olur.
            _log?.LogWarning(ex,
                "Cipher çözülemedi — DataProtection key-ring uyumsuz olabilir (Web+Api RACAR_DP_KEYS paylaşmalı).");
            return null;
        }
    }
}
