using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using RentACar.Web.Platform;

namespace RentACar.Web.Api.Platform;

/// <summary>
/// <c>/api/ui/v1/platform/*</c> — F12.1 platform console API (Blazor <c>/platform</c> parity: login, tenant console,
/// tenant detail, Document Center). A SEPARATE authority domain inside the <c>/api/ui/v1</c> group:
/// <list type="bullet">
/// <item><b>Access</b>: <see cref="PlatformClaims.Policy"/> (claim <c>platform_admin=true</c>, written only by a
/// platform login). A tenant user — Admin included — gets 403 <c>yetki_yok</c>; anonymous 401. The tenant permission
/// matrix does not apply (<c>IzinMuaf</c>), and the pilot gate is skipped (<see cref="UiApiExtensions.PilotExempt"/>).
/// The reverse direction (platform session → tenant <c>/api/ui</c>) stays closed by <see cref="PlatformIsolationMiddleware"/>.</item>
/// <item><b>Data</b>: only through <see cref="PlatformAdminService"/> (owner connection + tx-local <c>set_config</c> for
/// FORCE-RLS tables) — no new data path. Tenant META only: counts, status, contact fields, flags; never customer PII
/// or ledger rows.</item>
/// <item><b>CSRF, errors, no-store</b>: inherited from the group (X-XSRF-TOKEN header on every unsafe method).</item>
/// <item><b>Audit</b>: every tenant-scoped write lands in that tenant's <c>AuditLogs</c> (user <c>platform:&lt;op&gt;</c>)
/// inside the same transaction; Document Center actions (not tenant-scoped) keep the platform warning log.</item>
/// </list>
/// </summary>
public static partial class PlatformApi
{
    private const string Root = UiApiExtensions.PlatformPrefix;

    public const string ExemptReason =
        "Platform konsolu: firma izin matrisi uygulanmaz; erişim yalnız PlatformAdmin policy'siyle (platform_admin claim'i, " +
        "yalnız platform girişi yazar). Firma kullanıcısı (Admin dahil) 403; platform oturumu firma uçlarına PlatformIsolation ile kapalı.";

    public static RouteGroupBuilder MapPlatformApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/platform").WithTags("Platform")
            .RequireAuthorization(PlatformClaims.Policy)
            .PermissionExempt(ExemptReason);

        var session = g.MapGroup("/oturum");
        session.MapPost("/giris", Login).AllowAnonymous().RequireRateLimiting("login"); // brute-force: Blazor login policy
        session.MapPost("/cikis", Logout).AllowAnonymous(); // idempotent, also without a session
        session.MapGet("/ben", Me);

        g.MapGet("/ozet", Summary);
        MapTenants(g);
        MapDocuments(g);
        return g;
    }

    private static string OperatorName(HttpContext http) => http.User.Identity?.Name ?? "platform";

    private static ProblemHttpResult TenantNotFound() => F5Shared.NotFound("Firma bulunamadı.");

    // ================================================================== session

    private static async Task<Results<Ok<PlatformSessionResponse>, ProblemHttpResult>> Login(
        PlatformLoginRequest body, HttpContext http, PlatformCredentials creds)
    {
        if (!creds.Verify(body.Kullanici ?? "", body.Sifre ?? ""))
            // Which field is wrong is NOT disclosed. 400 (form error), not 401 (the SPA treats 401 as "session dropped").
            return UiError.Problem(UiError.Validation, "Kullanıcı adı veya şifre hatalı.");

        var principal = PlatformClaims.CreatePrincipal(creds.User);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        // ORDER MATTERS: the antiforgery token is bound to ctx.User — new principal first, then the token.
        http.User = principal;
        UiApiExtensions.IssueXsrf(http);
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RentACar.Web.Api.Platform")
            .LogWarning("PLATFORM: operatör {Operator} UI API ile giriş yaptı.", creds.User);
        return TypedResults.Ok(new PlatformSessionResponse(creds.User));
    }

    private static async Task<NoContent> Logout(HttpContext http, CancellationToken ct)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        UiApiExtensions.IssueXsrf(http); // token bound to the anonymous identity
        return TypedResults.NoContent();
    }

    private static Ok<PlatformSessionResponse> Me(HttpContext http, CancellationToken ct)
    {
        UiApiExtensions.IssueXsrf(http); // refreshed on page reload
        return TypedResults.Ok(new PlatformSessionResponse(OperatorName(http)));
    }

    // ================================================================== summary

    private static async Task<Ok<PlatformSummary>> Summary(PlatformAdminService svc, CancellationToken ct)
    {
        var rows = await svc.ListTenantsAsync(ct);
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);
        return TypedResults.Ok(new PlatformSummary(
            rows.Count,
            rows.Count(r => StatusOf(r.IsActive, r.KapanisTarihiUtc) == StatusActive),
            rows.Count(r => StatusOf(r.IsActive, r.KapanisTarihiUtc) == StatusPassive),
            rows.Count(r => StatusOf(r.IsActive, r.KapanisTarihiUtc) == StatusClosed),
            rows.Sum(r => r.UserCount), rows.Sum(r => r.AracSayisi), rows.Sum(r => r.AktifKira),
            rows.Count(r => r.CreatedAtUtc >= threshold)));
    }

    // ================================================================== status vocabulary

    public const string StatusActive = "Aktif";
    public const string StatusPassive = "Pasif";
    public const string StatusClosed = "Kapali";

    /// <summary>Badge precedence: the closed stamp wins regardless of IsActive (a DB anomaly cannot show "Aktif").</summary>
    public static string StatusOf(bool isActive, DateTimeOffset? closedAtUtc)
        => closedAtUtc is not null ? StatusClosed : isActive ? StatusActive : StatusPassive;
}
