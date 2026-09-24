using System.Security.Cryptography;

namespace RentACar.Application.TenantSettings;

/// <summary>
/// F11.1b güvenlik M6 — özel alan adı sahiplik doğrulaması. Kiracıya özel rastgele belirteç, alan adının DNS'inde
/// <c>_racar-verify.&lt;alan adı&gt;</c> TXT kaydı olarak yayınlanmadan kayıt <c>Active</c>'e geçmez. Eskiden ilk HTTP isteği
/// kaydı etkinleştiriyordu: DNS'i platforma yönelmiş BAŞKA bir firmanın alan adını önce ekleyen kiracı onu ele
/// geçiriyordu. Bekleyen kayıt <see cref="PendingLifetime"/> sonra süresi dolmuş sayılır.
/// </summary>
public static class DomainVerification
{
    public const string RecordPrefix = "_racar-verify.";

    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(7);

    /// <summary>Başka kiracının varlığını/durumunu sızdırmayan tek red mesajı.</summary>
    public const string CannotAddMessage =
        "Alan adı eklenemedi. Alan adı size aitse destek ekibiyle iletişime geçin.";

    /// <summary>Platformun kendi alt alan adı uzayı (TenantDomainRepository.EnsureSubdomainAsync ile aynı).</summary>
    public const string PlatformBaseDomain = "rentpro.com";

    private static readonly System.Text.RegularExpressions.Regex HostLabel =
        new("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Özel alan adı biçimi — TEK kural (Blazor "domain ekle" ve /api/ui aynı servis yolundan geçer): yalnız DNS adı
    /// (IP, port, yol, joker yok), en az iki etiket, harfli TLD. Platformun kendi alt alan adı uzayı
    /// (<c>*.rentpro.com</c>) reddedilir — aksi halde bir firma başka bir firmanın alt alan adını önceden tutup onun
    /// "Sitemi Aç"ını bozabiliyordu.
    /// </summary>
    public static string NormalizeCustomHost(string? host)
    {
        var h = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (h.Length == 0) throw new Common.ValidationException("Alan adı zorunludur.", "host");
        if (h.Length > 253 || System.Net.IPAddress.TryParse(h, out _))
            throw new Common.ValidationException("Alan adı geçerli bir DNS adı olmalıdır (ör. www.firmam.com).", "host");
        var labels = h.Split('.');
        if (labels.Length < 2 || !labels.All(HostLabel.IsMatch) || !labels[^1].All(char.IsAsciiLetter))
            throw new Common.ValidationException("Alan adı geçerli bir DNS adı olmalıdır (ör. www.firmam.com).", "host");
        if (h == PlatformBaseDomain || h.EndsWith("." + PlatformBaseDomain, StringComparison.Ordinal))
            throw new Common.ValidationException("Platform alan adı özel alan adı olarak eklenemez.", "host");
        return h;
    }

    public static string RecordName(string host) => RecordPrefix + host.Trim().TrimEnd('.').ToLowerInvariant();

    public static string NewToken() => "racar-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20));

    public static bool IsExpired(DateTimeOffset createdAtUtc, DateTimeOffset nowUtc) => nowUtc - createdAtUtc > PendingLifetime;

    /// <summary>TXT değerlerinden biri belirtece birebir eşit mi (boşluk/tırnak kırpılır, sabit zamanlı).</summary>
    public static bool Matches(IEnumerable<string> txtValues, string token)
        => txtValues.Any(v => CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(v.Trim().Trim('"')), System.Text.Encoding.UTF8.GetBytes(token)));
}

/// <summary>F11.1b güvenlik M6 — DNS TXT sorgusu portu (gerçek çözücü Infrastructure'da, testte sahte).</summary>
public interface IDnsTxtResolver
{
    /// <summary>Adın TXT değerleri (bulunamazsa ya da hata olursa boş liste — istisna sızmaz).</summary>
    Task<IReadOnlyList<string>> ResolveTxtAsync(string name, CancellationToken ct = default);
}
