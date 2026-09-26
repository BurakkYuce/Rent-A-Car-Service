using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — definition (catalog) table description for <see cref="CatalogCrud"/>. Business rules stay in the
/// service; the spec only wires list/get/create/update/delete plus the endpoint-layer limits.
/// </summary>
internal sealed class CatalogSpec<TService, TEntity, TRequest, TDto>
    where TService : class where TEntity : class where TRequest : ICatalogRequest
{
    public required string Path { get; init; }
    public required string Tag { get; init; }
    public required string NotFoundText { get; init; }
    public required Permission[] ReadPermissions { get; init; }
    public required Permission WritePermission { get; init; }
    public required Func<TService, CancellationToken, Task<IReadOnlyList<TEntity>>> List { get; init; }
    public required Func<TService, Guid, CancellationToken, Task<TEntity?>> Get { get; init; }
    public required Func<TService, Guid, CancellationToken, Task<string?>> Version { get; init; }
    public required Func<TEntity, string?, TDto> ToDto { get; init; }
    public required Func<TService, TRequest, HttpContext, CancellationToken, Task<Guid>> Create { get; init; }
    /// <summary>(service, id, body, stored row read before the lock, expected version, http, ct).</summary>
    public required Func<TService, Guid, TRequest, TEntity, string, HttpContext, CancellationToken, Task<bool>> Update { get; init; }
    public required Func<TService, Guid, CancellationToken, Task<bool>> Delete { get; init; }
    /// <summary>Endpoint limits; the second argument is the stored row on PUT (null on POST) so a rule stricter than
    /// the column runs only when the value CHANGED.</summary>
    public required Action<TRequest, TEntity?> Validate { get; init; }
    /// <summary>Free-text filter over the DTO (list <c>q</c>); null → no text filter.</summary>
    public required Func<TDto, string, bool> Matches { get; init; }
    public required SortFieldMap<TDto> Sort { get; init; }
    /// <summary>Service message prefix → JSON field (errors[field]).</summary>
    public required (string, string)[] FieldRules { get; init; }
}

/// <summary>Request bodies of catalog PUTs carry the opaque row version.</summary>
internal interface ICatalogRequest
{
    string? Surum { get; }
}

/// <summary>
/// F9.1 — generic list/get/create/replace/delete for the F9 definition tables (tarifeler, tarife matrisi, tarife
/// grupları, sigorta ürünleri, kira kuralları, broker yasakları, ek hizmetler, servis tanımları).
/// <list type="bullet">
/// <item>Tenant-wide tables (no branch column is a scope): RLS + query filter isolate tenants (another tenant's id → 404).</item>
/// <item>PUT is a full replacement: mandatory <c>surum</c>; version compared under a row lock
/// (<see cref="IRowVersionStore"/>) → 409 <c>cakisma</c>, nothing written.</item>
/// <item>List: <c>q</c> text filter, whitelist sorting, paging (definition tables are small — in memory).</item>
/// </list>
/// </summary>
internal static class CatalogCrud
{
    public static RouteGroupBuilder MapCatalog<TService, TEntity, TRequest, TDto>(
        this RouteGroupBuilder v1, CatalogSpec<TService, TEntity, TRequest, TDto> s)
        where TService : class where TEntity : class where TRequest : ICatalogRequest
    {
        var g = v1.MapGroup(s.Path).WithTags(s.Tag);

        Read(g.MapGet("", async Task<Ok<Sayfa<TDto>>> (string? q, int? sayfa, int? boyut, string? sirala, TService svc,
            CancellationToken ct) =>
        {
            var rows = (await s.List(svc, ct)).Select(e => s.ToDto(e, null));
            if (F5Shared.Nz(q) is { } text) rows = rows.Where(d => s.Matches(d, text));
            return TypedResults.Ok(F5Shared.Paginate(rows.ToList(), s.Sort, sayfa, boyut, sirala));
        }).MapFields(F5Shared.SortRules), s);

        Read(g.MapGet("/{id:guid}", async Task<Results<Ok<TDto>, ProblemHttpResult>> (Guid id, TService svc, CancellationToken ct)
            => await DetailAsync(s, svc, id, ct) is { } d ? TypedResults.Ok(d) : ServiceInsuranceShared.NotFound(s.NotFoundText)), s);

        g.MapPost("", async Task<Results<Created<TDto>, ProblemHttpResult>> (TRequest body, TService svc, HttpContext http,
            CancellationToken ct) =>
        {
            s.Validate(body, null);
            var id = await s.Create(svc, body, http, ct);
            return await DetailAsync(s, svc, id, ct) is { } d
                ? TypedResults.Created($"{UiApiExtensions.V1}{s.Path}/{id}", d)
                : ServiceInsuranceShared.NotFound(s.NotFoundText);
        }).MapFields(s.FieldRules).RequirePermission(s.WritePermission);

        g.MapPut("/{id:guid}", async Task<Results<Ok<TDto>, ProblemHttpResult>> (Guid id, TRequest body, TService svc,
            HttpContext http, CancellationToken ct) =>
        {
            if (await s.Get(svc, id, ct) is not { } existing) return ServiceInsuranceShared.NotFound(s.NotFoundText);
            var version = AracFinans.VehicleFinanceShared.Version(body.Surum);
            s.Validate(body, existing);
            if (!await s.Update(svc, id, body, existing, version, http, ct)) return ServiceInsuranceShared.NotFound(s.NotFoundText);
            return await DetailAsync(s, svc, id, ct) is { } d ? TypedResults.Ok(d) : ServiceInsuranceShared.NotFound(s.NotFoundText);
        }).MapFields(s.FieldRules).RequirePermission(s.WritePermission);

        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, TService svc, CancellationToken ct)
            => await s.Delete(svc, id, ct) ? TypedResults.NoContent() : ServiceInsuranceShared.NotFound(s.NotFoundText))
            .RequirePermission(s.WritePermission);
        return g;
    }

    private static RouteHandlerBuilder Read<TService, TEntity, TRequest, TDto>(RouteHandlerBuilder b,
        CatalogSpec<TService, TEntity, TRequest, TDto> s)
        where TService : class where TEntity : class where TRequest : ICatalogRequest
        => s.ReadPermissions.Length == 1 ? b.RequirePermission(s.ReadPermissions[0]) : b.RequireAnyPermission(s.ReadPermissions);

    /// <summary>Version is read BEFORE the fields (a concurrent write between the two reads yields an older version →
    /// the next PUT gets 409 instead of silently overwriting).</summary>
    private static async Task<TDto?> DetailAsync<TService, TEntity, TRequest, TDto>(
        CatalogSpec<TService, TEntity, TRequest, TDto> s, TService svc, Guid id, CancellationToken ct)
        where TService : class where TEntity : class where TRequest : ICatalogRequest
    {
        var version = await s.Version(svc, id, ct);
        return await s.Get(svc, id, ct) is { } e ? s.ToDto(e, version) : default;
    }
}
