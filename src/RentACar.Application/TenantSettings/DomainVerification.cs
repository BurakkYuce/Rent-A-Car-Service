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
