using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Tanim;

/// <summary>A definition row returned by the generic endpoints (JSON: <c>id</c>, <c>aktif</c>, …, <c>surum</c>).</summary>
public interface IDefinitionRow
{
    Guid Id { get; }
    bool Aktif { get; }
}

/// <summary>POST/PUT body of a definition; <c>surum</c> is REQUIRED on PUT (full replacement).</summary>
public interface IDefinitionRequest
{
    string? Surum { get; }
}

/// <summary>
/// F11.1a — description of ONE definition (master) table for <see cref="DefinitionEndpoints.MapDefinition"/>.
/// Everything entity-specific (service calls, DTO mapping, input limits, in-use check) is a delegate here; the
/// HTTP contract (routes, status codes, version handling, list filters) is written once.
/// Delegates receive the request's <see cref="IServiceProvider"/> (scoped services: tenant, user, cache).
/// </summary>
public sealed class DefinitionRoute<TEntity, TDto, TRequest>
    where TEntity : class
    where TDto : IDefinitionRow
    where TRequest : IDefinitionRequest
{
    /// <summary>Route under <c>/api/ui/v1</c>, e.g. <c>/markalar</c> (same as the Blazor page route).</summary>
    public required string Path { get; init; }
    public required string Tag { get; init; }
    /// <summary>Must equal the Blazor page policy (parity); the service guard is the second line.</summary>
    public required Permission Permission { get; init; }

    public required Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TEntity>>> List { get; init; }
    public required Func<IServiceProvider, Guid, CancellationToken, Task<TEntity?>> Get { get; init; }
    public required Func<IServiceProvider, Guid, CancellationToken, Task<string?>> GetVersion { get; init; }
    public required Func<IServiceProvider, CancellationToken, Task<IReadOnlyDictionary<Guid, string>>> GetVersions { get; init; }
    public required Func<IServiceProvider, TRequest, CancellationToken, Task<Guid>> Create { get; init; }
    /// <summary>(services, id, body, expectedVersion, ct) → false when the row is missing.</summary>
    public required Func<IServiceProvider, Guid, TRequest, string, CancellationToken, Task<bool>> Update { get; init; }
    public required Func<IServiceProvider, Guid, CancellationToken, Task<bool>> Delete { get; init; }

    public required Func<TEntity, Guid> IdOf { get; init; }
    /// <summary>(entity, version) → DTO. The version is null only where unknown.</summary>
    public required Func<TEntity, string?, TDto> ToDto { get; init; }
    /// <summary>Whitelisted sort fields (<c>sirala</c>); unknown field → 400.</summary>
    public required SiralamaHaritasi<TDto> Sort { get; init; }
    /// <summary>Texts matched by the <c>q</c> list filter (case-insensitive, Turkish culture).</summary>
    public required Func<TDto, IEnumerable<string?>> SearchText { get; init; }

    /// <summary>Column limits (varchar / numeric) checked at the edge → 400 <c>errors[alan]</c>, never a 500.</summary>
    public Action<TRequest>? ValidateLimits { get; init; }
    /// <summary>Service message prefix → JSON field (<see cref="AlanEsleme"/>).</summary>
    public IReadOnlyList<(string Onek, string Alan)> FieldRules { get; init; } = [];
    /// <summary>
    /// Non-null message = the row is referenced and must not be deleted (400 <c>dogrulama</c>; the SPA shows the
    /// message). Foreign-key references are caught generically by the repositories as well.
    /// </summary>
    public Func<IServiceProvider, TEntity, CancellationToken, Task<string?>>? InUse { get; init; }
    public string NotFoundMessage { get; init; } = "Kayıt bulunamadı.";
}

/// <summary>
/// F11.1a — ONE generic CRUD endpoint set for definition tables (the SPA "tanım CRUD" component contract,
/// <c>shared/form/tanim-crud/tanim-kaynagi.ts</c>):
/// <list type="bullet">
/// <item><c>GET kok</c> → full list as a JSON ARRAY (the component expects an array; definition tables are small).
/// Filters: <c>aktif</c> (true/false), <c>q</c> (text), <c>sirala</c> (whitelist; <c>-alan</c> descending).
/// Every row carries its <c>surum</c> so a row can be edited straight from the list.</item>
/// <item><c>GET kok/sayfa</c> → the same rows paged (<c>Sayfa</c>: kayitlar/toplam/sayfaNo/boyut).</item>
/// <item><c>GET kok/{id}</c>, <c>POST kok</c> (201), <c>PUT kok/{id}</c> (full replacement, <c>surum</c> REQUIRED,
/// stale → 409 <c>cakisma</c>), <c>DELETE kok/{id}</c> (204; in use → 400 with a "pasife alın" message).</item>
/// <item>Active/passive is the <c>aktif</c> field of the full PUT (history stays intact; delete is refused while used).</item>
/// </list>
/// Another tenant's id is 404 (RLS). Permission is written on the group (<c>IzinMetadata</c>, structural test).
/// </summary>
public static class DefinitionEndpoints
{
    private static readonly CompareInfo Tr = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public static RouteGroupBuilder MapDefinition<TEntity, TDto, TRequest>(
        this RouteGroupBuilder v1, DefinitionRoute<TEntity, TDto, TRequest> d)
        where TEntity : class
        where TDto : IDefinitionRow
        where TRequest : IDefinitionRequest
    {
        var g = v1.MapGroup(d.Path).WithTags(d.Tag).RequirePermission(d.Permission);

        g.MapGet("", async Task<Ok<IReadOnlyList<TDto>>> (bool? aktif, string? q, string? sirala, HttpContext http, CancellationToken ct)
            => TypedResults.Ok(Sort(d, await RowsAsync(d, http.RequestServices, aktif, q, ct), sirala)))
            .AlanlariEsle(F5Ortak.SiralamaKurallari);

        g.MapGet("/sayfa", async Task<Ok<Sayfa<TDto>>> (int? sayfa, int? boyut, bool? aktif, string? q, string? sirala,
                HttpContext http, CancellationToken ct)
            => TypedResults.Ok(F5Ortak.Sayfala(await RowsAsync(d, http.RequestServices, aktif, q, ct), d.Sort, sayfa, boyut, sirala)))
            .AlanlariEsle(F5Ortak.SiralamaKurallari);

        g.MapGet("/{id:guid}", async Task<Results<Ok<TDto>, ProblemHttpResult>> (Guid id, HttpContext http, CancellationToken ct)
            => await OneAsync(d, http.RequestServices, id, ct) is { } dto ? TypedResults.Ok(dto) : NotFound(d));

        g.MapPost("", async Task<Results<Created<TDto>, ProblemHttpResult>> (TRequest body, HttpContext http, CancellationToken ct) =>
        {
            var sp = http.RequestServices;
            d.ValidateLimits?.Invoke(body);
            var id = await d.Create(sp, body, ct);
            return await OneAsync(d, sp, id, ct) is { } dto
                ? TypedResults.Created($"{UiApiExtensions.V1}{d.Path}/{id}", dto)
                : NotFound(d);
        }).AlanlariEsle(d.FieldRules);

        g.MapPut("/{id:guid}", async Task<Results<Ok<TDto>, ProblemHttpResult>> (Guid id, TRequest body, HttpContext http, CancellationToken ct) =>
        {
            var sp = http.RequestServices;
            if (await d.Get(sp, id, ct) is null) return NotFound(d);
            if (string.IsNullOrWhiteSpace(body.Surum))
                throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
            d.ValidateLimits?.Invoke(body);
            if (!await d.Update(sp, id, body, body.Surum, ct)) return NotFound(d);
            return await OneAsync(d, sp, id, ct) is { } dto ? TypedResults.Ok(dto) : NotFound(d);
        }).AlanlariEsle(d.FieldRules);

        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var sp = http.RequestServices;
            if (await d.Get(sp, id, ct) is not { } entity) return NotFound(d);
            if (d.InUse is not null && await d.InUse(sp, entity, ct) is { } reason)
                throw new ValidationException(reason);
            return await d.Delete(sp, id, ct) ? TypedResults.NoContent() : NotFound(d);
        });

        return g;
    }

    private static ProblemHttpResult NotFound<TEntity, TDto, TRequest>(DefinitionRoute<TEntity, TDto, TRequest> d)
        where TEntity : class where TDto : IDefinitionRow where TRequest : IDefinitionRequest
        => F5Ortak.Bulunamadi(d.NotFoundMessage);

    /// <summary>The version is read BEFORE the fields: a concurrent write then yields a stale version (409 later),
    /// never fresh-looking stale fields.</summary>
    private static async Task<TDto?> OneAsync<TEntity, TDto, TRequest>(
        DefinitionRoute<TEntity, TDto, TRequest> d, IServiceProvider sp, Guid id, CancellationToken ct)
        where TEntity : class where TDto : IDefinitionRow where TRequest : IDefinitionRequest
    {
        var version = await d.GetVersion(sp, id, ct);
        return await d.Get(sp, id, ct) is { } e ? d.ToDto(e, version) : default;
    }

    private static async Task<IReadOnlyList<TDto>> RowsAsync<TEntity, TDto, TRequest>(
        DefinitionRoute<TEntity, TDto, TRequest> d, IServiceProvider sp, bool? active, string? q, CancellationToken ct)
        where TEntity : class where TDto : IDefinitionRow where TRequest : IDefinitionRequest
    {
        var versions = await d.GetVersions(sp, ct);
        var rows = (await d.List(sp, ct)).Select(e => d.ToDto(e, versions.GetValueOrDefault(d.IdOf(e))));
        if (active is { } a) rows = rows.Where(r => r.Aktif == a);
        if (F5Ortak.Nz(q) is { } text)
            rows = rows.Where(r => d.SearchText(r).Any(s => s is not null && Tr.IndexOf(s, text, CompareOptions.IgnoreCase) >= 0));
        return rows.ToList();
    }

    private static IReadOnlyList<TDto> Sort<TEntity, TDto, TRequest>(
        DefinitionRoute<TEntity, TDto, TRequest> d, IReadOnlyList<TDto> rows, string? sort)
        where TEntity : class where TDto : IDefinitionRow where TRequest : IDefinitionRequest
        => string.IsNullOrWhiteSpace(sort) ? rows : d.Sort.Uygula(rows.AsQueryable(), sort).ToList();
}
