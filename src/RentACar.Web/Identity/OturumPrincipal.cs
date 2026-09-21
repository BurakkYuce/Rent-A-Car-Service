using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using RentACar.Infrastructure.Identity;

namespace RentACar.Web.Identity;

/// <summary>
/// Doğrulanmış girişten cookie principal'ı üretir. Blazor girişi (<c>/auth/login</c>) ve yeni arayüz
/// girişi (<c>/api/ui/v1/oturum/giris</c>) AYNI fonksiyonu kullanır — iki giriş yolunun claim seti
/// ayrışırsa izin/şube/tenant kararları arayüze göre değişirdi (güvenlik-kritik, çoğaltılmaz).
/// </summary>
public static class OturumPrincipal
{
    public static ClaimsPrincipal Olustur(LoginResult result)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, result.User.UserName),
            new(ClaimTypes.Role, result.User.Rol.ToString()),
            new(IdentityClaims.AssignedBranch, result.User.AtanmisSube ?? ""),
            new(IdentityClaims.AssignedBranchId, result.User.AtanmisSubeId?.ToString() ?? ""), // FAZ 5-C1
            new(IdentityClaims.UserId, result.User.Id.ToString()),
            new(IdentityClaims.TenantId, result.Tenant.Id.ToString()),
            new(IdentityClaims.TenantCode, result.Tenant.Code),
        };
        // Kullanıcı-bazlı istisnalar: izin adı başına BİR claim (CSV değil — FindAll ile
        // ayrıştırmasız okunur). Değişiklik sonraki girişte etkinleşir (claim login'de donar).
        claims.AddRange(result.EkIzinler.Select(i => new Claim(IdentityClaims.IzinEk, i)));
        claims.AddRange(result.YasakIzinler.Select(i => new Claim(IdentityClaims.IzinYasak, i)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
