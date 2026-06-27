using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;

namespace RentACar.Api.Endpoints;

/// <summary>Kira sözleşmesi JSON API: doğrudan oluştur + teslim/dönüş + iptal. Double-booking
/// DB exclusion constraint ile garanti → çakışma 409. Yazma OperationsWrite; tenant izolasyonu JWT→RLS.</summary>
public static class RentalsApi
{
    public static IEndpointRouteBuilder MapRentalsApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/rentals").WithTags("Rentals").RequireAuthorization();

        grp.MapGet("/", async (RentalService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(RentalResponse.From)));

        grp.MapGet("/{id:guid}", async (Guid id, RentalService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } r ? Results.Ok(RentalResponse.From(r)) : NotFound());

        grp.MapPost("/", async (BookingRequest req, RentalService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateDirectAsync(req.ToInput(), ct);
            return Results.Created($"/api/v1/rentals/{id}", RentalResponse.From((await svc.GetAsync(id, ct))!));
        }).RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/deliver", async (Guid id, DeliverRequest req, RentalService svc, CancellationToken ct) =>
            await svc.DeliverAsync(id, req.CikisKm, req.CikisYakit, ct)
                ? Results.Ok(RentalResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/return", async (Guid id, ReturnRequest req, RentalService svc, CancellationToken ct) =>
            await svc.ReturnAsync(id, req.DonusKm, req.DonusYakit, req.GercekDonus, ct)
                ? Results.Ok(RentalResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/cancel", async (Guid id, RentalService svc, CancellationToken ct) =>
            await svc.CancelAsync(id, ct) ? Results.Ok(RentalResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        return app;
    }

    private static IResult NotFound()
        => Results.Json(new ApiError("not_found", "Kira sözleşmesi bulunamadı."), statusCode: StatusCodes.Status404NotFound);
}
