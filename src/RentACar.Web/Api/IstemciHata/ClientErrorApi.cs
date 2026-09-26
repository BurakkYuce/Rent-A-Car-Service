using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.IstemciHata;

/// <summary>
/// <c>POST /api/ui/v1/istemci-hata</c> (F3.3) — yeni arayüzün yakalanmamış tarayıcı hatası raporu.
/// <list type="bullet">
/// <item><b>Yalnız oturum:</b> grubun varsayılan <c>RequireAuthorization</c>'ı; anonim → 401 <c>oturum_yok</c>.
/// İzin kapısı yok (<see cref="AuthExtensions.PermissionExempt{TBuilder}"/>): her oturum KENDİ hatasını raporlar,
/// uç veri okumaz/yazmaz, yalnız log. Pilot kapısından muaf (<see cref="UiApiExtensions.PilotExempt"/>).</item>
/// <item><b>Sınırlar:</b> gövde en çok <see cref="BodyLimit"/> bayt (fazlası 413, gövde belleğe alınmaz),
/// alanlar ayrıca kırpılır, IP başına hız sınırı (<see cref="RatePolicy"/>). CSRF grubun filtresinden.</item>
/// <item><b>Log:</b> WARNING, firma + kullanıcıyla. Kontrol karakterleri temizlenir (sahte log satırı yok).</item>
/// </list>
/// </summary>
public static class ClientErrorApi
{
    public const int BodyLimit = 4 * 1024;
    public const string RatePolicy = "istemci-hata";

    /// <summary>Alan sınırları (istemci de aynı sınırlarla kırpar).</summary>
    public const int MessageLimit = 500, StackLimit = 2500, UrlLimit = 300, VersionLimit = 64;

    public sealed record IstemciHataIstegi(string? Mesaj, string? Yigin, string? Url, string? Surum);

    public static RouteGroupBuilder MapClientErrorApi(this RouteGroupBuilder v1)
    {
        v1.MapPost("/istemci-hata", Save)
            .PermissionExempt("İstemci hata raporu: her oturum yalnız KENDİ tarayıcı hatasını raporlar; uç veri okumaz/yazmaz, " +
                      "yalnız WARNING loglar (gövde ≤ 4 KB, IP başına hız sınırı).")
            .RequireRateLimiting(RatePolicy)
            .Accepts<IstemciHataIstegi>("application/json")
            .WithTags("Kabuk");
        return v1;
    }

    // İkinci parametre (CancellationToken) ŞART: yalnız HttpContext alan yöntem grubu RequestDelegate'e bağlanır,
    // dönüş değeri yok sayılır (OturumApi'deki tuzak notu).
    private static async Task<Results<NoContent, ProblemHttpResult>> Save(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is > BodyLimit) return TooLarge();

        // Gövde sınırlı okunur: Content-Length yoksa (chunked) da en çok GovdeSiniri+1 bayt belleğe alınır.
        var tampon = new byte[BodyLimit + 1];
        var readValue = 0;
        while (readValue < tampon.Length)
        {
            var n = await http.Request.Body.ReadAsync(tampon.AsMemory(readValue), ct);
            if (n == 0) break;
            readValue += n;
        }
        if (readValue > BodyLimit) return TooLarge();

        IstemciHataIstegi? request;
        try
        {
            request = JsonSerializer.Deserialize<IstemciHataIstegi>(tampon.AsSpan(0, readValue), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return UiError.Problem(UiError.Validation, "Hata raporu okunamadı.");
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Mesaj))
            return UiError.Problem(UiError.Validation, "Hata mesajı zorunludur.", "mesaj");

        var u = http.User;
        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RentACar.Web.Api.IstemciHata")
            .LogWarning(
                "İstemci hatası: {Mesaj} | url={Url} surum={Surum} firma={TenantId} kullanici={UserId} ({KullaniciAdi}){Yigin}",
                Clear(request.Mesaj, MessageLimit, lineEnd: false),
                Clear(request.Url, UrlLimit, lineEnd: false),
                Clear(request.Surum, VersionLimit, lineEnd: false),
                u.FindFirst(IdentityClaims.TenantId)?.Value,
                u.FindFirst(IdentityClaims.UserId)?.Value,
                u.Identity?.Name,
                request.Yigin is { Length: > 0 } y ? "\n" + Clear(y, StackLimit, lineEnd: true) : "");
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult TooLarge()
        => TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "İstek çok büyük",
            detail: $"Hata raporu en çok {BodyLimit} bayt olabilir.");

    /// <summary>Kırpar; kontrol karakterlerini boşluğa çevirir (yığında yalnız satır sonu korunur).</summary>
    internal static string Clear(string? text, int limit, bool lineEnd)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var brief = text.Length > limit ? text[..limit] : text;
        var sb = new StringBuilder(brief.Length);
        foreach (var c in brief)
            sb.Append(char.IsControl(c) && !(lineEnd && c == '\n') ? ' ' : c);
        return sb.ToString();
    }
}
