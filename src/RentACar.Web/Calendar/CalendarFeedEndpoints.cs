namespace RentACar.Web.Calendar;

/// <summary>
/// iCal feed (kimliksiz, token). Token yenileme F13'te Blazor formuyla birlikte kalktı; yeni arayüz
/// <c>/api/ui/v1</c> takvim aboneliği ucunu kullanır.
/// </summary>
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

        return app;
    }
}
