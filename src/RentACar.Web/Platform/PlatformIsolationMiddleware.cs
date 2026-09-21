using RentACar.Web.Identity;

namespace RentACar.Web.Platform;

/// <summary>
/// Alan ayrımı: platform operatörü (claim <c>platform_admin=true</c>) tenant UYGULAMASINA giremez.
/// Tek cookie şeması olduğu için platform admin authenticated'tır ve rol'süz `[Authorize]` tenant
/// sayfaları (Panel, araç listesi, kira formu…) tenant_id claim'i olmadan açılırdı — RLS her şeyi boş
/// döndürdüğü için VERİ SIZMAZ ama yarı-render boş sayfa kafa karıştırır (iki yetki alanı ayrık olmalı).
/// Platform operatörü tenant verisini yalnız KONSOLDAN (owner bağlantısı, cross-tenant) yönetir; tenant
/// UI'ına hiç ihtiyacı yok. Çözüm: platform admin bir tenant sayfasına giderse konsola geri yönlendir.
/// Ters yön (tenant kullanıcısı → /platform) zaten PlatformAdmin policy'siyle kapalı.
/// Skip-path'ler TenantActiveMiddleware ile AYNI (kanıtlanmış set): altyapı yolları atlanır — özellikle
/// /auth (logout ÇALIŞMALI) ve /platform (konsolun kendisi → döngü olmasın).
/// </summary>
public sealed class PlatformIsolationMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && ctx.User.HasClaim(PlatformClaims.PlatformAdmin, "true")
            && !ShouldSkip(ctx.Request.Path))
        {
            // F1.2: yeni arayüz API'si konsola yönlendirilmez (JSON istemcisi 302 izlemez) → 403 ProblemDetails.
            if (RentACar.Web.Api.UiApiExtensions.UiYolu(ctx.Request.Path))
            {
                await RentACar.Web.Api.UiApiExtensions.YazAsync(ctx, RentACar.Web.Api.UiHata.YetkiYok,
                    "Platform operatörü firma arayüzünü kullanamaz.");
                return;
            }
            ctx.Response.Redirect("/platform/tenants");
            return;
        }
        await next(ctx);
    }

    private static bool ShouldSkip(PathString path)
    {
        var p = path.Value ?? "";
        return p.StartsWith("/platform", StringComparison.OrdinalIgnoreCase)  // konsolun kendisi
            || p.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)        // logout ÇALIŞMALI
            || p.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            // F1.2: yeni arayüzün oturum uçları (giriş/çıkış çalışmalı; ben → 401 "firma oturumu yok").
            || RentACar.Web.Api.UiApiExtensions.OturumYolu(path)
            || Path.HasExtension(p); // statik varlıklar (.css/.js/.woff2 …) — sayfa değil
    }
}
