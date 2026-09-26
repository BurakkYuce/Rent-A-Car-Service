using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Web.Identity;

/// <summary>Cookie claim adları (login endpoint'i yazar, identity buradan okur).</summary>
public static class IdentityClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantCode = "tenant_code";
    public const string UserId = "user_id";
    public const string AssignedBranch = "assigned_sube";
    public const string AssignedBranchId = "assigned_sube_id"; // FAZ 5-C1
    public const string PermissionExtra = "izin_ek";       // kullanıcı-bazlı EK izin (izin adı başına bir claim)
    public const string PermissionDenied = "izin_yasak"; // kullanıcı-bazlı YASAK izin
    // Rol, standart ClaimTypes.Role olarak yazılır → [Authorize(Roles="Admin")] doğrudan çalışır.
}

/// <summary>
/// ITenantContext + ICurrentUser'ı HttpContext.User'dan (cookie claim'leri) çözer.
/// Araç ekranları static SSR olduğundan her etkileşim gerçek bir HTTP isteğidir →
/// HttpContext (ve tenant claim'i) daima mevcuttur. Anonim isteklerde null → RLS
/// default-deny.
/// </summary>
public sealed class HttpContextIdentity(IHttpContextAccessor accessor) : ITenantContext, ICurrentUser
{
    private ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public Guid? TenantId
        => Guid.TryParse(User.FindFirst(IdentityClaims.TenantId)?.Value, out var g) ? g : null;

    public Guid? UserId
        => Guid.TryParse(User.FindFirst(IdentityClaims.UserId)?.Value, out var g) ? g : null;

    public string? UserName
        => User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;

    public UserRole? Role
        => Enum.TryParse<UserRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;

    public string? AssignedBranch
    {
        get
        {
            var v = User.FindFirst(IdentityClaims.AssignedBranch)?.Value;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
    }

    public Guid? AssignedBranchId
        => Guid.TryParse(User.FindFirst(IdentityClaims.AssignedBranchId)?.Value, out var g) ? g : null;

    public IReadOnlyCollection<string> EkIzinler
        => User.FindAll(IdentityClaims.PermissionExtra).Select(c => c.Value).ToArray();

    public IReadOnlyCollection<string> YasakIzinler
        => User.FindAll(IdentityClaims.PermissionDenied).Select(c => c.Value).ToArray();
}

/// <summary>
/// Hibrit kimlik: tenant/kullanıcıyı HER ERİŞİMDE (sorgu anında) CANLI çözer — HttpContext VARSA (static
/// SSR) ondan; YOKSA (interaktif circuit) circuit init'te doldurulmuş <see cref="CircuitTenantContext"/>'ten.
/// Bu, mod-seçen factory'nin tuzağını (ITenantContext'i handshake anında çözüp HttpContextIdentity'yi
/// ÖNBELLEĞE alması → circuit sorgusunda canlı-null → RLS deny → 0 satır) çözer: örnek scope'ta önbeklense
/// bile property'ler sorgu anında değerlendiği için circuit'te doğru tenant'ı verir.
/// </summary>
public sealed class HybridIdentity(IHttpContextAccessor accessor, CircuitTenantContext circuit)
    : ITenantContext, ICurrentUser
{
    // COALESCE: önce HttpContext claim'ini dene; DEĞER VARSA kullan (static SSR); yoksa (circuit ya da
    // ANONİM/null HttpContext) circuit-captured değere düş. Böylece circuit'te HttpContext non-null-anonim
    // olsa bile (bilinen gotcha) tenant null'a düşmez → RLS default-deny'a takılmaz.
    private ClaimsPrincipal? U => accessor.HttpContext?.User;

    public Guid? TenantId
        => (Guid.TryParse(U?.FindFirst(IdentityClaims.TenantId)?.Value, out var g) ? g : (Guid?)null)
           ?? circuit.TenantId;

    public Guid? UserId
        => (Guid.TryParse(U?.FindFirst(IdentityClaims.UserId)?.Value, out var g) ? g : (Guid?)null)
           ?? circuit.UserId;

    public string? UserName
        => (U?.Identity?.IsAuthenticated == true ? (U.FindFirst(ClaimTypes.Name)?.Value ?? U.Identity?.Name) : null)
           ?? circuit.UserName;

    public UserRole? Role
        => (Enum.TryParse<UserRole>(U?.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : (UserRole?)null)
           ?? circuit.Role;

    public string? AssignedBranch
    {
        get
        {
            var v = U?.FindFirst(IdentityClaims.AssignedBranch)?.Value;
            return string.IsNullOrWhiteSpace(v) ? circuit.AssignedBranch : v;
        }
    }

    public Guid? AssignedBranchId
        => (Guid.TryParse(U?.FindFirst(IdentityClaims.AssignedBranchId)?.Value, out var g) ? g : (Guid?)null)
           ?? circuit.AssignedBranchId;

    // İstisnalarda COALESCE anahtarı KİMLİĞİN varlığı (izin listesinin doluluğu değil):
    // istisnasız kullanıcının claim'i meşru olarak BOŞTUR — boşluğu "claim yok" sayıp circuit'e
    // düşmek yanlış olmaz ama kimliksiz HttpContext'te iki kaynağı karıştırmamak için tek kapı.
    public IReadOnlyCollection<string> EkIzinler
        => U?.Identity?.IsAuthenticated == true
            ? U.FindAll(IdentityClaims.PermissionExtra).Select(c => c.Value).ToArray()
            : circuit.EkIzinler;

    public IReadOnlyCollection<string> YasakIzinler
        => U?.Identity?.IsAuthenticated == true
            ? U.FindAll(IdentityClaims.PermissionDenied).Select(c => c.Value).ToArray()
            : circuit.YasakIzinler;
}

/// <summary>
/// Kimlik durumu sağlayıcısı (hibrit). Kimliği YAPICI'da yakalar:
///  • static SSR: her istek yeni scope → HttpContext taze → doğru kullanıcı.
///  • interaktif circuit: scope SignalR handshake'inde kurulur → HttpContext O AN mevcut (auth cookie'li)
///    → yakalanıp önbelleğe alınır; circuit sonrası HttpContext null olsa da önbellek doğru kalır.
/// Böylece <c>CircuitTenantContext</c>'i besleyen cascading auth-state circuit içinde de gerçek
/// kullanıcıyı taşır (RLS-in-circuit ön koşulu). CANLI okumaz — önbelleklenmiş tek yakalama.
/// </summary>
public sealed class SsrAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthenticationState _state;

    public SsrAuthenticationStateProvider(IHttpContextAccessor accessor)
        => _state = new AuthenticationState(
            accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity()));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
}
