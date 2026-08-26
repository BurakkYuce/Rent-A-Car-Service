using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Calendar;

/// <summary>iCal feed (kimliksiz, token) + token yenileme (girişli).</summary>
public static class CalendarFeedEndpoints
{
    public static IEndpointRouteBuilder MapCalendarFeedEndpoints(this IEndpointRouteBuilder app)
    {
        // Kimliksiz .ics — Google Calendar periyodik çeker. Yalnız token doğrularsa döner; izolasyon serviste.
        app.MapGet("/feed/calendar/{token}.ics", async (string token, CalendarFeedService svc, CancellationToken ct) =>
        {
            var ics = await svc.BuildAsync(token, ct);
            return ics is null ? Results.NotFound() : Results.Text(ics, "text/calendar; charset=utf-8");
        }).AllowAnonymous();

        // Token yenile — girişli kullanıcı kendi feed'ini iptal edip yeni URL üretir.
        app.MapPost("/takvim/yenile", async (HttpContext http, CalendarTokenService svc, CancellationToken ct) =>
        {
            var uid = http.User.FindFirst(IdentityClaims.UserId)?.Value;
            if (!Guid.TryParse(uid, out var userId)) return Results.Unauthorized();
            await svc.RegenerateAsync(userId, ct);
            return Sonuc.Tamam("/takvim-abonelik?yeni=1", "Yenilendi.");
        }).RequireAuthorization().AntiforgeryByEnv();

        return app;
    }
}
