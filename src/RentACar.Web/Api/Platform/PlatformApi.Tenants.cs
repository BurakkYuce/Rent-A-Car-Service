using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Platform;

namespace RentACar.Web.Api.Platform;

public static partial class PlatformApi
{
    /// <summary>Logo request cap: 1 MB logo (<see cref="LogoValidationRules.MaxBytes"/>) + multipart overhead (Blazor parity).</summary>
    private const long LogoRequestLimit = 2_000_000;

    /// <summary>
    /// Tenant code = login key: lowercase ASCII letters/digits and inner hyphens, 2–64 characters (column is 64).
    /// Stricter than the Blazor form (which only bounded the length) so new codes cannot differ from existing
    /// ones by case or whitespace; existing codes are untouched.
    /// </summary>
    private static readonly Regex CodeFormat = new("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailFormat = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void MapTenants(RouteGroupBuilder g)
    {
        var t = g.MapGroup("/kiracilar");
        t.MapGet("", ListTenants).MapFields(F5Shared.SortRules);
        t.MapGet("/secim", TenantOptions);
        t.MapGet("/{id:guid}", TenantDetail);
        t.MapPost("", CreateTenant).MapFields(CreateRules);
        t.MapPut("/{id:guid}", UpdateTenant).MapFields(UpdateRules);
        t.MapPost("/{id:guid}/durum", ChangeStatus);
        // F13.1b: /{id}/yeni-arayuz-pilot kaldırıldı (pilot kapısı yok; yeni arayüz herkes için).
        t.MapPost("/{id:guid}/web-sitesi-modulu", SetWebSiteModule);
        t.MapGet("/{id:guid}/logo", LogoContent)
            .Produces(StatusCodes.Status200OK, typeof(byte[]), "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);
        t.MapPost("/{id:guid}/logo", UploadLogo).DisableAntiforgery() // CSRF: group header filter (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(LogoRequestLimit))
            .MapFields(LogoRules);
        t.MapDelete("/{id:guid}/logo", DeleteLogo);
    }

    private static readonly (string, string)[] CreateRules =
    [
        ("Firma kodu", "kod"), ("'", "kod"), ("Firma adı", "ad"), ("Admin kullanıcı", "adminKullanici"),
        ("Admin parolası", "adminSifre"),
    ];

    private static readonly (string, string)[] UpdateRules =
    [
        ("Firma adı", "ad"), ("Yetkili adı", "yetkiliAd"), ("E-posta", "eposta"), ("Telefon", "telefon"),
        ("Notlar", "notlar"), ("Plan", "plan"),
    ];

    private static readonly (string, string)[] LogoRules = [("Logo", "logo")];

    // ================================================================== list / options / detail

    private sealed record ListRow(PlatformTenantRow Row, string Status);

    private static readonly SortFieldMap<ListRow> SortMap = SortFieldMap<ListRow>
        .Create(r => r.Row.Id)
        .Alan("kod", r => r.Row.Code)
        .Alan("ad", r => r.Row.Name)
        .Alan("durum", r => r.Status)
        .Alan("kullaniciSayisi", r => r.Row.UserCount)
        .Alan("aracSayisi", r => r.Row.AracSayisi)
        .Alan("aktifKira", r => r.Row.AktifKira)
        .Alan("sonGiris", r => r.Row.SonGiris)
        .Alan("olusturma", r => r.Row.CreatedAtUtc)
        .Default("kod");

    /// <summary>Tenant console. Filters: <c>q</c> (code/name contains, case-insensitive), <c>durum</c>
    /// (<c>Aktif</c>|<c>Pasif</c>|<c>Kapali</c>). The tenant count is small (platform scale): filtered in memory.</summary>
    private static async Task<Ok<Sayfa<PlatformTenantRowDto>>> ListTenants(
        PlatformAdminService svc, string? q, string? durum, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var status = ParseStatus(durum, "durum");
        var request = new ListeIstegi(Math.Min(sayfa ?? 1, 1_000_000), boyut ?? 50, sirala);
        var query = F5Shared.Nz(q);
        var rows = (await svc.ListTenantsAsync(ct))
            .Select(r => new ListRow(r, StatusOf(r.IsActive, r.KapanisTarihiUtc)))
            .Where(r => status is null || r.Status == status)
            .Where(r => query is null
                        || r.Row.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || r.Row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var page = SortMap.Apply(rows.AsQueryable(), request.Sirala)
            .Skip((int)request.Atla).Take(request.Boyut)
            .Select(r => ToRowDto(r.Row)).ToList();
        return TypedResults.Ok(new Sayfa<PlatformTenantRowDto>(page, rows.Count, request.Sayfa, request.Boyut));
    }

    private static PlatformTenantRowDto ToRowDto(PlatformTenantRow r) => new(
        r.Id, r.Code, r.Name, StatusOf(r.IsActive, r.KapanisTarihiUtc), r.KapanisTarihiUtc, r.CreatedAtUtc,
        r.UserCount, r.AracSayisi, r.AktifKira, r.SonGiris);

    private static async Task<Ok<IReadOnlyList<PlatformTenantOptionDto>>> TenantOptions(PlatformAdminService svc, CancellationToken ct)
        => TypedResults.Ok<IReadOnlyList<PlatformTenantOptionDto>>((await svc.ListTenantOptionsAsync(ct))
            .Select(o => new PlatformTenantOptionDto(o.Id, o.Code, o.Name, StatusOf(o.IsActive, o.KapanisTarihiUtc)))
            .ToList());

    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> TenantDetail(
        Guid id, PlatformAdminService svc, CancellationToken ct)
        => await LoadDetailAsync(svc, id, ct) is { } d ? TypedResults.Ok(d) : TenantNotFound();

    private static async Task<PlatformTenantDetailDto?> LoadDetailAsync(PlatformAdminService svc, Guid id, CancellationToken ct)
    {
        if (await svc.GetTenantAsync(id, ct) is not { } d) return null;
        var (logoBytes, eval) = await svc.GetTenantLogoAsync(id, ct);
        var logo = logoBytes is { Length: > 0 } && eval is { } e
            ? new PlatformLogoDto(true, e.Bayt, e.Genislik, e.Yukseklik, e.BaskiyaUygun, e.Uyari)
            : new PlatformLogoDto(false, null, null, null, false, null);
        return new PlatformTenantDetailDto(
            d.Id, d.Code, d.Name, StatusOf(d.IsActive, d.KapanisTarihiUtc), d.KapanisTarihiUtc, d.CreatedAtUtc,
            d.UpdatedAtUtc, PlatformAdminService.TenantVersion(d.CreatedAtUtc, d.UpdatedAtUtc),
            d.YetkiliAd, d.Eposta, d.Telefon, d.Notlar, d.Plan,
            d.UserCount, d.AracSayisi, d.AktifKira, d.ToplamKira, d.SonGiris, d.Gelir30Gun,
            d.WebSitesiModulu, d.YeniArayuzPilot, d.PublicSiteEnabled,
            d.Domainler.Select(x => new PlatformDomainDto(x.Host, x.Tur, x.Durum)).ToList(),
            logo);
    }

    /// <summary>After a write: the fresh detail (the row exists — it was just written).</summary>
    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> DetailAfterWrite(
        PlatformAdminService svc, Guid id, CancellationToken ct)
        => await LoadDetailAsync(svc, id, ct) is { } d ? TypedResults.Ok(d) : TenantNotFound();

    private static string? ParseStatus(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        foreach (var s in new[] { StatusActive, StatusPassive, StatusClosed })
            if (string.Equals(value.Trim(), s, StringComparison.OrdinalIgnoreCase)) return s;
        throw new ValidationException("Durum Aktif, Pasif ya da Kapali olmalıdır.", field);
    }
}
