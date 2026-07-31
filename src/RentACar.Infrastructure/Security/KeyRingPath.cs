namespace RentACar.Infrastructure.Security;

/// <summary>
/// DataProtection key-ring dizininin çözümü — TEK yer, test edilebilir.
///
/// <para><b>Neden ayrı sınıf:</b> eski çözüm <c>DependencyInjection</c> içine gömülüydü ve
/// <c>RACAR_DP_KEYS</c> yoksa <c>AppContext.BaseDirectory/dp-keys</c>'e, yani <b>build çıktısının
/// içine</b> düşüyordu. Sonucu ampirik olarak görüldü: bir <c>obj/bin</c> temizliği key-ring'i
/// sildi ve yereldeki 29 cipher (17 TC + 12 pasaport) <b>kalıcı olarak çözülemez</b> hale geldi —
/// düz metin kolonları <c>PiiBackfill</c> zaten null'lamıştı, yani geri dönüş yok. Aynı desen
/// üretimde "publish dizinini üzerine yaz" akışında birebir tekrarlar.</para>
///
/// <para>Ek olarak <c>BaseDirectory</c> tabanlı yol, aynı makinedeki her binary'ye (Web / PublicSite /
/// testler) <b>AYRI</b> key-ring verir; birinin şifrelediğini diğeri çözemez. Üretimde doğru çözüm
/// tek paylaşılan dizindir → <c>RACAR_DP_KEYS</c> (Web/Api/PublicSite'ta dev-dışı ZORUNLU).</para>
/// </summary>
public static class KeyRingPath
{
    public const string EnvVar = "RACAR_DP_KEYS";

    /// <summary>
    /// Sıra: <c>RACAR_DP_KEYS</c> → kullanıcı profili altında SABİT dizin. Dönen yol asla build
    /// çıktısının (<c>bin/</c>) altında değildir.
    /// </summary>
    /// <param name="envValue">Test için enjekte edilir; null ise ortam değişkeni okunur.</param>
    public static string Resolve(string? envValue = null)
    {
        var acik = envValue ?? Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(acik)) return acik.Trim();

        // Dev/test fallback'i: profil dizini kalıcıdır ve `dotnet clean` onu silmez.
        var taban = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(taban))
            taban = Path.GetTempPath();   // profilsiz ortam (kimi CI konteynerleri) — üretimde guard zaten env şart koşuyor

        return Path.Combine(taban, "racar", "dp-keys");
    }
}
