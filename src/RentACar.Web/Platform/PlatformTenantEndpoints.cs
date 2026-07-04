using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Platform;

/// <summary>Tenant aç/kapa + oluştur uçları — PlatformAdmin policy + antiforgery.</summary>
public static class PlatformTenantEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/platform/tenants")
            .RequireAuthorization(PlatformClaims.Policy)
            .AntiforgeryByEnv();

        grp.MapPost("/toggle", async (HttpContext http, PlatformAdminService svc,
            [FromForm] Guid id, [FromForm] bool active) =>
        {
            try
            {
                await svc.SetActiveAsync(id, active, http.User.Identity?.Name ?? "platform");
                return Results.Redirect("/platform/tenants?ok=1");
            }
            catch (ValidationException ex) // ör. olmayan tenant id (adversarial L1: 500 yerine mesaj)
            {
                return Results.Redirect("/platform/tenants?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        grp.MapPost("/create", async (HttpContext http, PlatformAdminService svc,
            [FromForm] string code, [FromForm] string name,
            [FromForm] string adminUser, [FromForm] string adminPassword) =>
        {
            try
            {
                await svc.CreateTenantAsync(code, name, adminUser, adminPassword, http.User.Identity?.Name ?? "platform");
                return Results.Redirect("/platform/tenants?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect("/platform/tenants?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        return app;
    }
}
