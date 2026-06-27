using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;

namespace RentACar.Api.Endpoints;

/// <summary>Araç JSON CRUD. Okuma: kimlik doğrulanmış; yazma: OperationsWrite (403). Tenant
/// izolasyonu JWT → ApiIdentity → RLS ile otomatik.</summary>
public static class VehiclesApi
{
    public static IEndpointRouteBuilder MapVehiclesApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/vehicles").WithTags("Vehicles").RequireAuthorization();

        // Sayfalı + filtreli liste: ?q=&durum=&grup=&page=&pageSize=
        grp.MapGet("/", async (VehicleService svc, CancellationToken ct,
            string? q = null, VehicleStatus? durum = null, string? grup = null, int page = 1, int pageSize = 20) =>
        {
            var filter = new VehicleFilter { Query = q, Durum = durum, Grup = grup, Page = page, PageSize = pageSize };
            return Results.Ok(PagedResponse.From(await svc.SearchAsync(filter, ct), VehicleResponse.From));
        });

        grp.MapGet("/{id:guid}", async (Guid id, VehicleService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v
                ? Results.Ok(VehicleResponse.From(v))
                : Results.Json(new ApiError("not_found", "Araç bulunamadı."), statusCode: StatusCodes.Status404NotFound));

        grp.MapPost("/", async (VehicleRequest req, VehicleService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateAsync(req.ToInput(), ct);
            var created = await svc.GetAsync(id, ct);
            return Results.Created($"/api/v1/vehicles/{id}", VehicleResponse.From(created!));
        }).RequirePermission(Permission.OperationsWrite);

        grp.MapPut("/{id:guid}", async (Guid id, VehicleRequest req, VehicleService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req.ToInput(), ct)
                ? Results.Ok(VehicleResponse.From((await svc.GetAsync(id, ct))!))
                : Results.Json(new ApiError("not_found", "Araç bulunamadı."), statusCode: StatusCodes.Status404NotFound))
            .RequirePermission(Permission.OperationsWrite);

        grp.MapDelete("/{id:guid}", async (Guid id, VehicleService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct)
                ? Results.NoContent()
                : Results.Json(new ApiError("not_found", "Araç bulunamadı."), statusCode: StatusCodes.Status404NotFound))
            .RequirePermission(Permission.OperationsWrite);

        return app;
    }
}
