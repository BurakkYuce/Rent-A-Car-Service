namespace RentACar.Web.Identity;

/// <summary>
/// Cookie şemasının 401/403 yönlendirme hedefini belirler.
///
/// <para><b>Neden ayrı sınıf:</b> bu karar <c>Program.cs</c> içinde lambda olarak duruyordu ve
/// test edilemiyordu. Saf fonksiyona çıkarıldı → davranış birim testiyle kilitlenir.</para>
///
/// <para><b>Neden 403 artık <c>/login</c>'e GİTMEZ (canlı hata):</b> yetkisiz bir POST 403 üretiyor,
/// <c>AccessDeniedPath = "/login"</c> kullanıcıyı login'e atıyor, ama kullanıcı ZATEN giriş yapmış
/// olduğu için <c>Login.razor</c> onu uygulamaya — yani <c>/</c>'a — yönlendiriyordu. Sonuç: hiçbir
/// açıklama olmadan ana ekrana düşmek. Kullanıcının "durduk yere ana ekrana atıyor" şikayetinin
/// üç kök nedeninden biri buydu. Artık 403 → <c>/yetkisiz</c>: kullanıcı ne olduğunu görüyor.</para>
/// </summary>
public static class YetkiYonlendirme
{
    /// <summary>Platform alanı ayrı bir kabuk kullanır; oradaki 401/403 kendi login'ine gider.</summary>
    private static bool Platform(PathString yol) => yol.StartsWithSegments("/platform");

    /// <summary>401 (kimlik yok) hedefi.</summary>
    public static string GirisHedefi(PathString yol) => Platform(yol) ? "/platform/login" : "/login";

    /// <summary>403 (kimlik var, yetki yok) hedefi.</summary>
    public static string YetkisizHedefi(PathString yol) => Platform(yol) ? "/platform/login" : "/yetkisiz";
}
