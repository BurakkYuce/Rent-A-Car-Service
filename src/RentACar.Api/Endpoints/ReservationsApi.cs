using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;

namespace RentACar.Api.Endpoints;

/// <summary>Rezervasyon JSON API + durum geçişleri (onayla/iptal/kiraya çevir). Geçersiz geçiş →
/// 400 (hata zarfı); çakışma → 409. Yazma OperationsWrite; tenant izolasyonu JWT→RLS.</summary>
public static class ReservationsApi
{
    public static IEndpointRouteBuilder MapReservationsApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/reservations").WithTags("Reservations").RequireAuthorization();

        grp.MapGet("/", async (ReservationService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(ReservationResponse.From)));

        grp.MapGet("/{id:guid}", async (Guid id, ReservationService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } r
                ? Results.Ok(ReservationResponse.From(r))
                : NotFound());

        grp.MapPost("/", async (BookingRequest req, ReservationService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateAsync(req.ToInput(), ct);
            return Results.Created($"/api/v1/reservations/{id}", ReservationResponse.From((await svc.GetAsync(id, ct))!));
        }).RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/confirm", async (Guid id, ReservationService svc, CancellationToken ct) =>
            await svc.ConfirmAsync(id, ct) ? Results.Ok(ReservationResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/cancel", async (Guid id, ReservationService svc, CancellationToken ct) =>
            await svc.CancelAsync(id, ct) ? Results.Ok(ReservationResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/convert", async (Guid id, ReservationService svc, CancellationToken ct) =>
        {
            var rentalId = await svc.ConvertToRentalAsync(id, ct);
            return Results.Created($"/api/v1/rentals/{rentalId}", new { rentalId });
        }).RequirePermission(Permission.OperationsWrite);

        return app;
    }

    private static IResult NotFound()
        => Results.Json(new ApiError("not_found", "Rezervasyon bulunamadı."), statusCode: StatusCodes.Status404NotFound);
}
