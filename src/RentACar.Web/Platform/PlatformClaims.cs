using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RentACar.Web.Platform;

/// <summary>Platform operatörü claim adları + policy adı (tek doğruluk kaynağı).</summary>
public static class PlatformClaims
{
    /// <summary>Platform süper-admin işareti — YALNIZ platform girişi (Blazor <c>/platform/auth/login</c> ve
    /// UI API <c>/api/ui/v1/platform/oturum/giris</c>) yazar; tenant login'i ASLA.</summary>
    public const string PlatformAdmin = "platform_admin";

    /// <summary>Authorization policy adı.</summary>
    public const string Policy = "PlatformAdmin";

    /// <summary>
    /// Platform operator principal — shared by both login paths so the claim set cannot drift. Claims are ONLY the
    /// name + <c>platform_admin=true</c>: tenant_id / role are deliberately absent, so ITenantContext stays null and
    /// RLS denies every tenant row.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(string userName)
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, userName), new Claim(PlatformAdmin, "true")],
            CookieAuthenticationDefaults.AuthenticationScheme));
}
