using System.Security.Cryptography;

namespace RentACar.Web.Identity;

/// <summary>
/// Yapılandırılmamış geliştirme kimlikleri (seed kullanıcıları, platform operatörü) için ÇALIŞMA ANINDA
/// üretilen parola. <b>Repoda sabit parola YOK</b>: eski sabit seed parolası sahibinin gerçek bir dış
/// sistemdeki parolasıyla aynıydı ve kodda + belgelerde düz metin duruyordu (GitGuardian da işaretler).
/// Kalıcı çit: <c>SabitParolaYokTests</c>.
/// </summary>
public static class GelistirmeParolasi
{
    // Karıştırılabilir karakterler (0/O, 1/l/I) yok — parola log satırından elle okunup yazılıyor.
    private const string Alfabe = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>20 karakter, kriptografik rastgele (~115 bit).</summary>
    public static string Uret() => RandomNumberGenerator.GetString(Alfabe, 20);
}
