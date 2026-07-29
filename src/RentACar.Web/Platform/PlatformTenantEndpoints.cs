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

        // PR-12: "Web Sitesi" modülü (satın alma kararı). Tenant detay sayfasından açılır/kapatılır.
        grp.MapPost("/modul-web-sitesi", async (HttpContext http, PlatformAdminService svc,
            [FromForm] Guid id, [FromForm] bool aktif) =>
        {
            try
            {
                await svc.SetWebSitesiModuluAsync(id, aktif, http.User.Identity?.Name ?? "platform");
                return Results.Redirect($"/platform/tenants/{id}?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/platform/tenants/{id}?hata=" + Uri.EscapeDataString(ex.Message));
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

        // ---- Konsol v2: bilgi güncelle / kapat / yeniden aç (detay sayfası formları) ----

        grp.MapPost("/update", async (HttpContext http, PlatformAdminService svc,
            [FromForm] Guid id, [FromForm] string name, [FromForm] string? yetkiliAd,
            [FromForm] string? eposta, [FromForm] string? telefon, [FromForm] string? notlar,
            [FromForm] string? plan) =>
        {
            try
            {
                await svc.UpdateTenantAsync(id, name, yetkiliAd, eposta, telefon, notlar, plan,
                    http.User.Identity?.Name ?? "platform");
                return Results.Redirect($"/platform/tenants/{id}?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/platform/tenants/{id}?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        // Kapat = kod-yazdırmalı onay: onayKod, tenant.Code ile SUNUCUDA eşleşmeli (data-confirm'den
        // bir kademe sert — müşteri kilitlemek tek tıka fazla hafif). Veri silinmez; Yeniden Aç var.
        grp.MapPost("/close", async (HttpContext http, PlatformAdminService svc,
            [FromForm] Guid id, [FromForm] string? onayKod) =>
        {
            try
            {
                var detay = await svc.GetTenantAsync(id) ?? throw new ValidationException("Tenant bulunamadı.");
                if (!string.Equals((onayKod ?? "").Trim(), detay.Code, StringComparison.Ordinal))
                    return Results.Redirect($"/platform/tenants/{id}?hata=onay"); // kod uyuşmadı — kapatılmadı
                await svc.CloseAsync(id, http.User.Identity?.Name ?? "platform");
                return Results.Redirect($"/platform/tenants/{id}?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/platform/tenants/{id}?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        grp.MapPost("/reopen", async (HttpContext http, PlatformAdminService svc, [FromForm] Guid id) =>
        {
            try
            {
                await svc.ReopenAsync(id, http.User.Identity?.Name ?? "platform");
                return Results.Redirect($"/platform/tenants/{id}?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/platform/tenants/{id}?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        return app;
    }
}
