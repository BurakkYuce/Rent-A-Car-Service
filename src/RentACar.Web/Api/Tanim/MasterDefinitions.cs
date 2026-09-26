using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Tanim;

/// <summary>Kod + Ad + Aktif definition row (every <see cref="MasterDefinitionService{T}"/> table).</summary>
public sealed record DefinitionDto(Guid Id, string Kod, string Ad, bool Aktif, string? Surum) : IDefinitionRow;

/// <summary>POST/PUT body of a Kod + Ad + Aktif definition; <c>surum</c> REQUIRED on PUT.</summary>
public sealed record DefinitionRequest(string? Kod, string? Ad, bool Aktif = true, string? Surum = null) : IDefinitionRequest;

/// <summary>
/// F11.1a — <see cref="DefinitionRoute{TEntity,TDto,TRequest}"/> factory for the <see cref="MasterDefinitionService{T}"/>
/// family (O12d base: cache + invalidate, OperationsWrite guard, Kod upper/trim, Kod uniqueness). Only the
/// create/update calls differ per entity (each service has its own input type); the rest is shared.
/// </summary>
public static class MasterDefinitions
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly SortFieldMap<DefinitionDto> Sort = SortFieldMap<DefinitionDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("aktif", x => x.Aktif);

    /// <param name="singularName">Service message noun, lower case ("marka", "iptal sebebi").</param>
    /// <param name="create">Service create call with the entity's own input type.</param>
    /// <param name="update">Service versioned update call (<c>UpdateAsync(id, input, expectedVersion)</c>).</param>
    public static DefinitionRoute<T, DefinitionDto, DefinitionRequest> For<T, TService>(
        string path, string tag, string singularName,
        Func<TService, DefinitionRequest, CancellationToken, Task<Guid>> create,
        Func<TService, Guid, DefinitionRequest, string, CancellationToken, Task<bool>> update,
        Permission permission = Permission.OperationsWrite,
        Func<IServiceProvider, T, CancellationToken, Task<string?>>? inUse = null)
        where T : class, IMasterDefinition, new()
        where TService : MasterDefinitionService<T>
    {
        var title = char.ToUpper(singularName[0], Tr) + singularName[1..];
        return new DefinitionRoute<T, DefinitionDto, DefinitionRequest>
        {
            Path = path,
            Tag = tag,
            Permission = permission,
            List = (sp, ct) => S<T, TService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<T, TService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<T, TService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<T, TService>(sp).GetVersionsAsync(ct),
            Create = (sp, body, ct) => create(S<T, TService>(sp), body, ct),
            Update = (sp, id, body, version, ct) => update(S<T, TService>(sp), id, body, version, ct),
            Delete = (sp, id, ct) => S<T, TService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, version) => new DefinitionDto(e.Id, e.Kod, e.Ad, e.Aktif, version),
            Sort = Sort,
            SearchText = r => [r.Kod, r.Ad],
            ValidateLimits = Limits,
            // Service messages: "{Title} kodu …", "'X' kodlu … zaten var.", "{Title} adı …".
            FieldRules = [($"{title} kodu", "kod"), ("'", "kod"), ($"{title} adı", "ad")],
            InUse = inUse,
            NotFoundMessage = $"{title} bulunamadı.",
        };
    }

    /// <summary>Column limits of every Kod+Ad master (<c>MasterConfigs</c>: Kod varchar(32), Ad varchar(128)).
    /// Kod is also checked by the service (same message); Ad is not, so without this a 129-char name is a 22001.</summary>
    public static void Limits(DefinitionRequest r)
    {
        RentalLimits.Text(r.Kod, 32, "kod", "Kod");
        RentalLimits.Text(r.Ad, 128, "ad", "Ad");
    }

    private static TService S<T, TService>(IServiceProvider sp) where T : class, IMasterDefinition, new() where TService : MasterDefinitionService<T>
        => sp.GetRequiredService<TService>();
}
