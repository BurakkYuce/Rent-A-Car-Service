using RentACar.Application.Authorization;

namespace RentACar.Web.Identity;

/// <summary>
/// Endpoint gruplarını yetki matrisine (RolePermissions) göre rol-gate eder. Matris tek
/// doğruluk kaynağı → web ve servis guard'ları aynı kuralı paylaşır.
/// </summary>
public static class AuthExtensions
{
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder group, Permission permission)
        => group.RequireAuthorization(p => p.RequireRole(RolePermissions.RolesWith(permission)));

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
