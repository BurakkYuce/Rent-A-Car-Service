using Microsoft.AspNetCore.Identity;

namespace RentACar.Web.Platform;

/// <summary>
/// Platform süper-admin operatörü kimliği — config'ten (`Platform:AdminUser` + `Platform:AdminPasswordHash`).
/// Tenant kullanıcısı DEĞİL (DB'de User yok); tenant'lardan bağımsız tek operatör. Hash, ASP.NET
/// <see cref="PasswordHasher{T}"/> ile üretilir/doğrulanır (rastgele gömülü tuz → user parametresi önemsiz).
/// Üretimde ZORUNLU (Program.cs açılışta reddeder); dev'de sabit varsayılan.
/// </summary>
public sealed class PlatformCredentials(string user, string passwordHash)
{
    private static readonly PasswordHasher<object> Hasher = new();

    public string User { get; } = user;

    /// <summary>Config hash'i üretmek için (bootstrap komutu): verilen parolanın hash'i.</summary>
    public static string HashPassword(string password) => Hasher.HashPassword(new object(), password);

    /// <summary>Sabit-zamanlı: kullanıcı adı + parola doğru mu.</summary>
    public bool Verify(string enteredUser, string enteredPassword)
        => string.Equals(enteredUser, User, StringComparison.Ordinal)
           && Hasher.VerifyHashedPassword(new object(), passwordHash, enteredPassword) != PasswordVerificationResult.Failed;
}
