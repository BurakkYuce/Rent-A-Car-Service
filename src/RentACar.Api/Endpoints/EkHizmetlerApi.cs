using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.EkHizmetler;

namespace RentACar.Api.Endpoints;

/// <summary>Ek hizmet tanımı master JSON CRUD. Okuma: kimlik doğrulanmış; yazma: OperationsWrite.
/// Tenant izolasyonu JWT→ApiIdentity→RLS. Master sözlük (sayfalamasız liste).</summary>
public static class EkHizmetlerApi
{
    public static IEndpointRouteBuilder MapEkHizmetlerApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/ek-hizmetler").WithTags("EkHizmetler").RequireAuthorization();

        grp.MapGet("/", async (EkHizmetTanimService svc, CancellationToken ct, bool aktif = false) =>
            Results.Ok((aktif ? await svc.ListActiveAsync(ct) : await svc.ListAsync(ct)).Select(EkHizmetTanimResponse.From)));

        grp.MapGet("/{id:guid}", async (Guid id, EkHizmetTanimService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } t ? Results.Ok(EkHizmetTanimResponse.From(t)) : NotFound());

        grp.MapPost("/", async (EkHizmetTanimRequest req, EkHizmetTanimService svc, CancellationToken ct) =>
        {
            var id = await svc.CreateAsync(req.ToInput(), ct);
            return Results.Created($"/api/v1/ek-hizmetler/{id}", EkHizmetTanimResponse.From((await svc.GetAsync(id, ct))!));
        }).RequirePermission(Permission.OperationsWrite);

        grp.MapPut("/{id:guid}", async (Guid id, EkHizmetTanimRequest req, EkHizmetTanimService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req.ToInput(), ct)
                ? Results.Ok(EkHizmetTanimResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapDelete("/{id:guid}", async (Guid id, EkHizmetTanimService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        return app;
    }

    private static IResult NotFound()
        => Results.Json(new ApiError("not_found", "Ek hizmet bulunamadı."), statusCode: StatusCodes.Status404NotFound);
}
