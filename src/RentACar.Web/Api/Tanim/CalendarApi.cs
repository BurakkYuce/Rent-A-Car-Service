using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Web.Calendar;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — the personal calendar subscription link (<c>/takvim-abonelik</c>, Blazor <c>TakvimAbonelik</c>): any
/// signed-in user, it is the user's OWN link (the token is tied to the session user, never to an id in the request).
/// </summary>
public static class CalendarApi
{
    public static void MapCalendarApi(this RouteGroupBuilder v1)
    {
        var cal = v1.MapGroup("/takvim-abonelik").WithTags("Takvim");
        cal.MapGet("", async Task<Results<Ok<CalendarLinkDto>, ProblemHttpResult>> (HttpContext http, CalendarTokenService s, CancellationToken ct)
                => UserId(http) is { } uid ? TypedResults.Ok(Link(http, await s.EnsureAsync(uid, ct))) : NoUser())
            .IzinMuaf("Kullanıcının KENDİ takvim bağlantısı; Blazor sayfası yalnız [Authorize].");
        // Regenerate = revoke the old link (it may have leaked). Not idempotent by design: each call issues a new token.
        cal.MapPost("/yenile", async Task<Results<Ok<CalendarLinkDto>, ProblemHttpResult>> (HttpContext http, CalendarTokenService s, CancellationToken ct)
                => UserId(http) is { } uid ? TypedResults.Ok(Link(http, await s.RegenerateAsync(uid, ct))) : NoUser())
            .IzinMuaf("Kullanıcının KENDİ takvim bağlantısını yeniler (eskisini iptal eder); Blazor ucu yalnız oturum ister.");
    }

    private static Guid? UserId(HttpContext http)
        => Guid.TryParse(http.User.FindFirst(IdentityClaims.UserId)?.Value, out var id) ? id : null;

    private static ProblemHttpResult NoUser()
        => UiHata.Problem(UiHata.OturumYok, "Oturum kullanıcısı bulunamadı.");

    /// <summary>Absolute feed URL (pasted into Google/Apple/Outlook) — same shape as the Blazor page.</summary>
    private static CalendarLinkDto Link(HttpContext http, string token)
        => new($"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/feed/calendar/{token}.ics");
}

/// <summary>Personal iCal feed link — personal, carries customer names/plates: never share.</summary>
public sealed record CalendarLinkDto(string Url);
