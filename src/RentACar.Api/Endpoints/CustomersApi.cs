using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.Customers;

namespace RentACar.Api.Endpoints;

/// <summary>Cari JSON CRUD. Okuma: kimlik doğrulanmış; yazma: OperationsWrite. Tenant izolasyonu
/// JWT → ApiIdentity → RLS. Benzersizlik ihlali (TC/Vergi No) → 409 (hata zarfı).</summary>
public static class CustomersApi
{
    public static IEndpointRouteBuilder MapCustomersApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/customers").WithTags("Customers").RequireAuthorization();

        // Sayfalı + aramalı liste: ?q=&page=&pageSize=
        grp.MapGet("/", async (CustomerService svc, CancellationToken ct,
            string? q = null, int page = 1, int pageSize = 20) =>
        {
            var filter = new CustomerFilter { Query = q, Page = page, PageSize = pageSize };
            return Results.Ok(PagedResponse.From(await svc.SearchAsync(filter, ct), CustomerResponse.From));
        });

        grp.MapGet("/{id:guid}", async (Guid id, CustomerService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } c
                ? Results.Ok(CustomerResponse.From(c))
                : Results.Json(new ApiError("not_found", "Cari bulunamadı."), statusCode: StatusCodes.Status404NotFound));

        grp.MapPost("/", async (CustomerRequest req, CustomerService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateAsync(req.ToInput(), ct);
            var created = await svc.GetAsync(id, ct);
            return Results.Created($"/api/v1/customers/{id}", CustomerResponse.From(created!));
        }).RequirePermission(Permission.OperationsWrite);

        grp.MapPut("/{id:guid}", async (Guid id, CustomerRequest req, CustomerService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req.ToInput(), ct)
                ? Results.Ok(CustomerResponse.From((await svc.GetAsync(id, ct))!))
                : Results.Json(new ApiError("not_found", "Cari bulunamadı."), statusCode: StatusCodes.Status404NotFound))
            .RequirePermission(Permission.OperationsWrite);

        grp.MapDelete("/{id:guid}", async (Guid id, CustomerService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct)
                ? Results.NoContent()
                : Results.Json(new ApiError("not_found", "Cari bulunamadı."), statusCode: StatusCodes.Status404NotFound))
            .RequirePermission(Permission.OperationsWrite);

        return app;
    }
}
