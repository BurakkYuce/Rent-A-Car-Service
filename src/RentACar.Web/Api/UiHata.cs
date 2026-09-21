using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;

namespace RentACar.Web.Api;

/// <summary>
/// <c>/api/ui/v1</c> hata sözleşmesi (F1.1): istisna → ProblemDetails
/// <c>{ type, title, status, detail, kod, errors?: { [alan]: string[] } }</c>.
/// SPA davranışı <c>kod</c>'a bağlıdır — HTTP durumu tek başına yetmez (iki ayrı 403, iki ayrı 409 var):
/// <list type="bullet">
/// <item><c>cakisma</c> (409): müsaitlik / TC / plaka gibi iş benzersizliği → form KORUNUR.</item>
/// <item><c>mukerrer</c> (409): aynı işlemin ikinci gönderimi → kayıt yeniden YÜKLENİR.</item>
/// <item><c>yetki_yok</c> (403): uyarı bandı; <c>pilot_degil</c> (403): kiracı yeni arayüzde değil.</item>
/// </list>
/// Boru hattına bağlanması F1.2'de (<c>UiApiExtensions</c>); burada yalnız saf eşleme + yazıcı.
/// </summary>
public static class UiHata
{
    public const string Dogrulama = "dogrulama";
    public const string YetkiYok = "yetki_yok";
    public const string PilotDegil = "pilot_degil";
    public const string Cakisma = "cakisma";
    public const string Mukerrer = "mukerrer";
    public const string OturumYok = "oturum_yok";
    public const string KiraciKapali = "kiraci_kapali";
    public const string CokIstek = "cok_istek";

    /// <summary>Kod tablosu: her <c>kod</c>'un HTTP durumu ve kısa Türkçe başlığı.</summary>
    public static readonly IReadOnlyDictionary<string, (int Status, string Baslik)> Tablo =
        new Dictionary<string, (int, string)>
        {
            [Dogrulama] = (StatusCodes.Status400BadRequest, "Doğrulama hatası"),
            [YetkiYok] = (StatusCodes.Status403Forbidden, "Yetki yok"),
            [PilotDegil] = (StatusCodes.Status403Forbidden, "Yeni arayüz bu firmada açık değil"),
            [Cakisma] = (StatusCodes.Status409Conflict, "Çakışma"),
            [Mukerrer] = (StatusCodes.Status409Conflict, "Mükerrer işlem"),
            [OturumYok] = (StatusCodes.Status401Unauthorized, "Oturum yok"),
            [KiraciKapali] = (StatusCodes.Status401Unauthorized, "Firma hesabı kapalı"),
            [CokIstek] = (StatusCodes.Status429TooManyRequests, "Çok fazla istek"),
        };

    /// <summary>
    /// İstisna → (durum, kod). Sıra ÖNEMLİ: alt tipler <see cref="ValidationException"/>'dan türediği
    /// için önce onlar denetlenir. Tanınmayan istisna → null (500 başka yerde, mesaj sızdırılmadan).
    /// </summary>
    public static (int Status, string Kod)? Esle(Exception ex) => ex switch
    {
        YetkiYokException => (StatusCodes.Status403Forbidden, YetkiYok),
        MukerrerIslemException => (StatusCodes.Status409Conflict, Mukerrer),
        AvailabilityConflictException or DuplicateCariException or DuplicatePlakaException
            => (StatusCodes.Status409Conflict, Cakisma),
        ValidationException => (StatusCodes.Status400BadRequest, Dogrulama),
        _ => null,
    };

    /// <summary>
    /// Eşlenen istisnayı ProblemDetails'e çevirir; <c>detail</c> = istisna mesajı, <c>errors</c> yalnız
    /// <see cref="ValidationException.Alan"/> doluysa. Eşlenmeyen istisna 500 döner ve mesajı SIZDIRMAZ.
    /// </summary>
    public static ProblemHttpResult Problem(Exception ex)
    {
        if (Esle(ex) is not { } e)
            return TypedResults.Problem(
                detail: "Beklenmeyen bir hata oluştu.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Sunucu hatası");

        return Problem(e.Kod, ex.Message, (ex as ValidationException)?.Alan);
    }

    /// <summary>Kod tablosundan doğrudan ProblemDetails (istisnasız adımlar için: oturum, pilot, hız sınırı).</summary>
    public static ProblemHttpResult Problem(string kod, string detay, string? alan = null)
    {
        var (status, baslik) = Tablo[kod];
        var ek = new Dictionary<string, object?> { ["kod"] = kod };
        if (!string.IsNullOrEmpty(alan))
            ek["errors"] = new Dictionary<string, string[]> { [alan] = [detay] };
        return TypedResults.Problem(detail: detay, statusCode: status, title: baslik, extensions: ek);
    }
}
