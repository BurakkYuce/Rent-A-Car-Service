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
/// Boru hattına bağlantı: <c>UiApiExtensions</c> (F1.2); burada yalnız saf eşleme + yazıcı.
/// </summary>
public static class UiError
{
    public const string Validation = "dogrulama";
    public const string Forbidden = "yetki_yok";
    public const string NotPilot = "pilot_degil";
    public const string ConflictCode = "cakisma";
    public const string Duplicate = "mukerrer";
    public const string NoSession = "oturum_yok";
    public const string TenantClosed = "kiraci_kapali";
    public const string TooManyRequests = "cok_istek";
    /// <summary>F1.2: CSRF başlığı (<c>X-XSRF-TOKEN</c>) yok ya da geçersiz (ör. girişten ÖNCE alınmış
    /// token). SPA davranışı: <c>GET /api/ui/v1/oturum/xsrf</c> ile token yenile, isteği BİR kez tekrarla.
    /// Ayrı kod çünkü <c>dogrulama</c> form hatası gösterir, <c>yetki_yok</c> uyarı bandı açar — ikisi de yanlış.</summary>
    public const string XsrfInvalid = "xsrf_gecersiz";

    /// <summary>Kod tablosu: her <c>kod</c>'un HTTP durumu ve kısa Türkçe başlığı.</summary>
    public static readonly IReadOnlyDictionary<string, (int Status, string Baslik)> Table =
        new Dictionary<string, (int, string)>
        {
            [Validation] = (StatusCodes.Status400BadRequest, "Doğrulama hatası"),
            [Forbidden] = (StatusCodes.Status403Forbidden, "Yetki yok"),
            [NotPilot] = (StatusCodes.Status403Forbidden, "Yeni arayüz bu firmada açık değil"),
            [ConflictCode] = (StatusCodes.Status409Conflict, "Çakışma"),
            [Duplicate] = (StatusCodes.Status409Conflict, "Mükerrer işlem"),
            [NoSession] = (StatusCodes.Status401Unauthorized, "Oturum yok"),
            [TenantClosed] = (StatusCodes.Status401Unauthorized, "Firma hesabı kapalı"),
            [TooManyRequests] = (StatusCodes.Status429TooManyRequests, "Çok fazla istek"),
            [XsrfInvalid] = (StatusCodes.Status400BadRequest, "Güvenlik belirteci geçersiz"),
        };

    /// <summary>
    /// İstisna → (durum, kod). Sıra ÖNEMLİ: alt tipler <see cref="ValidationException"/>'dan türediği
    /// için önce onlar denetlenir. Tanınmayan istisna → null (500 başka yerde, mesaj sızdırılmadan).
    /// </summary>
    public static (int Status, string Kod)? Map(Exception ex) => ex switch
    {
        NoPermissionException => (StatusCodes.Status403Forbidden, Forbidden),
        DuplicateOperationException => (StatusCodes.Status409Conflict, Duplicate),
        AvailabilityConflictException or DuplicateCariException or DuplicatePlakaException
            or ConcurrentModificationException // F4.3 adversarial F2: bayat sürüm — form korunur, kayıt yeniden okunur
            => (StatusCodes.Status409Conflict, ConflictCode),
        ValidationException => (StatusCodes.Status400BadRequest, Validation),
        _ when DataOverflow(ex) => (StatusCodes.Status400BadRequest, Validation),
        _ => null,
    };

    /// <summary>Veri taşmasında dönen genel mesaj — PostgreSQL iç ayrıntısı (kolon/tablo adı) SIZDIRILMAZ.</summary>
    public const string DataOverflowMessage = "Girilen değerlerden biri izin verilen büyüklüğü ya da uzunluğu aşıyor.";

    /// <summary>
    /// F4.4a adversarial MEDIUM-1 güvenlik ağı: istemci verisinin kolona sığmaması (PostgreSQL 22001
    /// string_data_right_truncation, 22003 numeric_value_out_of_range) 500 değil 400 <c>dogrulama</c>'dır.
    /// Uçlar sınırları zaten önceden denetler; bu ağ gözden kaçan ya da türetilmiş (ör. kira Tahsilat + delta)
    /// taşmaları yakalar. Yalnız bu iki SQLSTATE — diğer veritabanı hataları 500 kalır (hata gizlenmez).
    /// </summary>
    public static bool DataOverflow(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Npgsql.PostgresException { SqlState: "22001" or "22003" }) return true;
        return false;
    }

    /// <summary>
    /// Eşlenen istisnayı ProblemDetails'e çevirir; <c>detail</c> = istisna mesajı, <c>errors</c> yalnız
    /// <see cref="ValidationException.Alan"/> doluysa. Eşlenmeyen istisna 500 döner ve mesajı SIZDIRMAZ.
    /// </summary>
    public static ProblemHttpResult Problem(Exception ex)
    {
        if (Map(ex) is not { } e)
            return TypedResults.Problem(
                detail: "Beklenmeyen bir hata oluştu.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Sunucu hatası");

        if (ex is DuplicateOperationException { Existing: { } m })
            return Problem(e.Kod, ex.Message, alan: null, existing: m);
        return ex is ValidationException v
            ? Problem(e.Kod, v.Message, v.Alan)
            : Problem(e.Kod, DataOverflowMessage);
    }

    /// <summary>Kod tablosundan doğrudan ProblemDetails (istisnasız adımlar için: oturum, pilot, hız sınırı).</summary>
    public static ProblemHttpResult Problem(string code, string detail, string? alan = null, MevcutIslem? existing = null)
    {
        var (status, title) = Table[code];
        var extra = new Dictionary<string, object?> { ["kod"] = code };
        if (!string.IsNullOrEmpty(alan))
            extra["errors"] = new Dictionary<string, string[]> { [alan] = [detail] };
        // F4.4 adversarial HIGH-1: 409 mukerrer'de aynı anahtarla ZATEN yazılmış kayıt (id, belge no, tutar, döviz).
        // Anahtar adları sözleşmedir (OpenAPI: MukerrerProblemi) — ProblemDetails uzantısı adlandırma politikasına
        // bağlı kalmasın diye elle yazılır.
        if (existing is not null)
            extra["mevcut"] = new Dictionary<string, object?>
            {
                ["id"] = existing.Id, ["belgeNo"] = existing.BelgeNo, ["tutar"] = existing.Tutar, ["doviz"] = existing.Doviz,
                ["ayniIcerik"] = existing.AyniIcerik,
            };
        return TypedResults.Problem(detail: detail, statusCode: status, title: title, extensions: extra);
    }

    /// <summary>OpenAPI belgesi için 409 <c>mukerrer</c> gövdesinin biçimi (SPA tipi buradan üretilir). Yanıt
    /// gerçekte <see cref="Problem(string, string, string?, MevcutIslem?)"/> ile yazılır.</summary>
    public sealed record MukerrerProblemi(string Type, string Title, int Status, string Detail, string Kod, MevcutIslem? Mevcut);

    /// <summary>
    /// #271 L3 — kod tablosundan ProblemDetails + serbest <c>mevcut</c> uzantısı (ör. teklif kabul tekrarında
    /// ZATEN açılmış rezervasyon). <see cref="Problem(string, string, string?, MevcutIslem?)"/> ile aynı gövde
    /// (<c>kod</c> dahil); anahtar adları çağıranın sözleşmesidir, elle yazılır (adlandırma politikasına bağlı değil).
    /// </summary>
    public static ProblemHttpResult Problem(string code, string detail, IReadOnlyDictionary<string, object?> existing)
    {
        var (status, title) = Table[code];
        var extra = new Dictionary<string, object?> { ["kod"] = code, ["mevcut"] = existing };
        return TypedResults.Problem(detail: detail, statusCode: status, title: title, extensions: extra);
    }
}
