namespace RentACar.Application.Common;

/// <summary>
/// İyimser eşzamanlılık (F4.3 adversarial F2): istemcinin okuduğu kayıt sürümü, yazma anındaki (satır kilidi
/// ALTINDA okunan) sürümle aynı değil — kayıt bu arada başka bir oturumda ya da işlemde değişti. Tam
/// değiştirme (PUT) bayat anlık görüntüyle yazılsaydı başka oturumun değişikliği (ör. drop ücreti) sessizce
/// geri alınırdı.
/// <para>Yeni SPA'da 409 <c>cakisma</c>: form SİLİNMEZ; güncel kayıt yüklenir, kullanıcının dokunmadığı alanlar
/// güncellenir, dokunduğu alanlar korunur. <see cref="ValidationException"/>'dan türer (Blazor yakalayıcıları
/// aynen çalışır; Blazor bu kontrolü kullanmaz — sürüm göndermez).</para>
/// </summary>
public sealed class ConcurrentModificationException(string message) : ValidationException(message)
{
    public const string RentalMessage =
        "Kira başka bir oturumda değişti; güncel hâli yüklendi, değişikliklerinizi kontrol edip yeniden kaydedin.";

    /// <summary>F5.1 — kira dışı tam değiştirme uçları (rezervasyon, rez şartı, filo kiralama künyesi).</summary>
    public const string RecordMessage =
        "Kayıt başka bir oturumda değişti; güncel hâli yüklendi, değişikliklerinizi kontrol edip yeniden kaydedin.";

    /// <summary>F5.1 adversarial H1 — zaten kabul edilmiş teklife ikinci kabul (eşzamanlı ya da yanıtı kaybolan
    /// tekrar): 409 <c>cakisma</c>, ikinci rezervasyon AÇILMAZ; SPA teklifi yeniden yükler (ilk kabulün rezervasyonu
    /// detayda görünür).</summary>
    public const string QuotationAcceptMessage =
        "Teklif zaten kabul edildi ve rezervasyona çevrildi; ikinci rezervasyon açılmadı.";
}
