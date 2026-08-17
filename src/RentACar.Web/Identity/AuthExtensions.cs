using System.Security.Claims;
using RentACar.Application.Authorization;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Identity;

/// <summary>
/// Endpoint gruplarını ETKİN izne göre gate eder. 2026-08-17'ye kadar salt rol matrisiydi
/// (RequireRole); kullanıcı-bazlı istisnalarla karar EffectivePermission'a taşındı — matris +
/// claim'deki ek izin/yasak. Servis guard'ı AYNI kuralı ICurrentUser'dan ikinci kez uygular
/// (çift savunma korunur).
/// </summary>
public static class AuthExtensions
{
    /// <summary>ClaimsPrincipal üzerinden etkin izin kararı — sayfa policy'leri ve uç grupları
    /// aynı fonksiyonu kullanır (iki ayrı yorum OLAMAZ).</summary>
    public static bool HasPermission(ClaimsPrincipal user, Permission permission)
    {
        var role = Enum.TryParse<UserRole>(user.FindFirst(ClaimTypes.Role)?.Value, out var r)
            ? r : (UserRole?)null;
        return EffectivePermission.Has(role, permission,
            user.FindAll(IdentityClaims.IzinEk).Select(c => c.Value).ToArray(),
            user.FindAll(IdentityClaims.IzinYasak).Select(c => c.Value).ToArray());
    }

    /// <summary>Sayfa policy adı — <c>[Authorize(Policy = ...)]</c> sabitleri buradan türetilir.</summary>
    public static string PolicyName(Permission permission) => $"izin:{permission}";

    /// <summary>Program.cs: her Permission için isimli policy kaydeder (sayfa attribute'ları için).</summary>
    public static void AddPermissionPolicies(this Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        foreach (var p in Enum.GetValues<Permission>())
            options.AddPolicy(PolicyName(p), b => b.RequireAssertion(ctx => HasPermission(ctx.User, p)));
    }

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder group, Permission permission)
        => group.RequireAuthorization(p => p.RequireAssertion(ctx => HasPermission(ctx.User, permission)));

    /// <summary>Tekil uç için etkin-izin kapısı (grup kapısından daha dar bir izin gerektiğinde —
    /// ör. OperationsWrite grubundaki /sil ucu OperationsDelete ister).</summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder endpoint, Permission permission)
        => endpoint.RequireAuthorization(p => p.RequireAssertion(ctx => HasPermission(ctx.User, permission)));

    /// <summary>
    /// PR-12: "Web Sitesi" modülü satın alınmamışsa uç grubunu 404'e çevirir (403 değil — modülün
    /// varlığı da bilgi). Menüyü gizlemek YALNIZ görseldir; modülü almayan tenant URL'i elle yazıp
    /// ilan yazabilir ve satın aldığı gün her şey ANİDEN yayına girerdi. CLAUDE.md §6: "guard GİRİŞ
    /// noktasında + doğrulama SUNUCUDA".
    /// </summary>
    public static RouteGroupBuilder RequireWebSitesiModulu(this RouteGroupBuilder group)
        => group.AddEndpointFilter(async (ctx, next) =>
        {
            var tenant = ctx.HttpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId;
            if (tenant is not { } id) return Results.NotFound();
            var cache = ctx.HttpContext.RequestServices.GetRequiredService<TenantStatusCache>();
            if (!await cache.WebSitesiModuluAsync(id, ctx.HttpContext.RequestAborted))
                return Results.NotFound();
            return await next(ctx);
        });

    /// <summary>
    /// Antiforgery'yi ORTAMA göre uygular (roadmap E2, review #7): PROD'da token ZORUNLU (CSRF koruması),
    /// dev/test'te gevşek (geliştirme kolaylığı). Tüm form-POST grupları DisableAntiforgery() yerine bunu
    /// çağırır → tek anahtarla yönetilir. Formlar <AntiforgeryToken/> taşır (prod'da doğrulanır).
    /// </summary>
    public static TBuilder AntiforgeryByEnv<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => FormSecurity.EnforceAntiforgery ? builder : builder.DisableAntiforgery();
}

/// <summary>Antiforgery zorunluluğu anahtarı — Program startup'ta ortamdan (IsProduction) set edilir.</summary>
public static class FormSecurity
{
    public static bool EnforceAntiforgery { get; set; }
}
