using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.IstemciHata;

/// <summary>
/// <c>POST /api/ui/v1/istemci-hata</c> (F3.3) — yeni arayüzün yakalanmamış tarayıcı hatası raporu.
/// <list type="bullet">
/// <item><b>Yalnız oturum:</b> grubun varsayılan <c>RequireAuthorization</c>'ı; anonim → 401 <c>oturum_yok</c>.
/// İzin kapısı yok (<see cref="AuthExtensions.IzinMuaf{TBuilder}"/>): her oturum KENDİ hatasını raporlar,
/// uç veri okumaz/yazmaz, yalnız log. Pilot kapısından muaf (<see cref="UiApiExtensions.PilotMuaf"/>).</item>
/// <item><b>Sınırlar:</b> gövde en çok <see cref="GovdeSiniri"/> bayt (fazlası 413, gövde belleğe alınmaz),
/// alanlar ayrıca kırpılır, IP başına hız sınırı (<see cref="HizPolitikasi"/>). CSRF grubun filtresinden.</item>
/// <item><b>Log:</b> WARNING, firma + kullanıcıyla. Kontrol karakterleri temizlenir (sahte log satırı yok).</item>
/// </list>
/// </summary>
public static class IstemciHataApi
{
    public const int GovdeSiniri = 4 * 1024;
    public const string HizPolitikasi = "istemci-hata";

    /// <summary>Alan sınırları (istemci de aynı sınırlarla kırpar).</summary>
    public const int MesajSiniri = 500, YiginSiniri = 2500, UrlSiniri = 300, SurumSiniri = 64;

    public sealed record IstemciHataIstegi(string? Mesaj, string? Yigin, string? Url, string? Surum);

    public static RouteGroupBuilder MapIstemciHataApi(this RouteGroupBuilder v1)
    {
        v1.MapPost("/istemci-hata", Kaydet)
            .IzinMuaf("İstemci hata raporu: her oturum yalnız KENDİ tarayıcı hatasını raporlar; uç veri okumaz/yazmaz, " +
                      "yalnız WARNING loglar (gövde ≤ 4 KB, IP başına hız sınırı).")
            .RequireRateLimiting(HizPolitikasi)
            .Accepts<IstemciHataIstegi>("application/json")
            .WithTags("Kabuk");
        return v1;
    }

    // İkinci parametre (CancellationToken) ŞART: yalnız HttpContext alan yöntem grubu RequestDelegate'e bağlanır,
    // dönüş değeri yok sayılır (OturumApi'deki tuzak notu).
    private static async Task<Results<NoContent, ProblemHttpResult>> Kaydet(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is > GovdeSiniri) return CokBuyuk();

        // Gövde sınırlı okunur: Content-Length yoksa (chunked) da en çok GovdeSiniri+1 bayt belleğe alınır.
        var tampon = new byte[GovdeSiniri + 1];
        var okunan = 0;
        while (okunan < tampon.Length)
        {
            var n = await http.Request.Body.ReadAsync(tampon.AsMemory(okunan), ct);
            if (n == 0) break;
            okunan += n;
        }
        if (okunan > GovdeSiniri) return CokBuyuk();

        IstemciHataIstegi? istek;
        try
        {
            istek = JsonSerializer.Deserialize<IstemciHataIstegi>(tampon.AsSpan(0, okunan), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return UiHata.Problem(UiHata.Dogrulama, "Hata raporu okunamadı.");
        }
        if (istek is null || string.IsNullOrWhiteSpace(istek.Mesaj))
            return UiHata.Problem(UiHata.Dogrulama, "Hata mesajı zorunludur.", "mesaj");

        var u = http.User;
        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RentACar.Web.Api.IstemciHata")
            .LogWarning(
                "İstemci hatası: {Mesaj} | url={Url} surum={Surum} firma={TenantId} kullanici={UserId} ({KullaniciAdi}){Yigin}",
                Temizle(istek.Mesaj, MesajSiniri, satirSonu: false),
                Temizle(istek.Url, UrlSiniri, satirSonu: false),
                Temizle(istek.Surum, SurumSiniri, satirSonu: false),
                u.FindFirst(IdentityClaims.TenantId)?.Value,
                u.FindFirst(IdentityClaims.UserId)?.Value,
                u.Identity?.Name,
                istek.Yigin is { Length: > 0 } y ? "\n" + Temizle(y, YiginSiniri, satirSonu: true) : "");
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult CokBuyuk()
        => TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "İstek çok büyük",
            detail: $"Hata raporu en çok {GovdeSiniri} bayt olabilir.");

    /// <summary>Kırpar; kontrol karakterlerini boşluğa çevirir (yığında yalnız satır sonu korunur).</summary>
    internal static string Temizle(string? metin, int sinir, bool satirSonu)
    {
        if (string.IsNullOrEmpty(metin)) return "";
        var kisa = metin.Length > sinir ? metin[..sinir] : metin;
        var sb = new StringBuilder(kisa.Length);
        foreach (var c in kisa)
            sb.Append(char.IsControl(c) && !(satirSonu && c == '\n') ? ' ' : c);
        return sb.ToString();
    }
}
