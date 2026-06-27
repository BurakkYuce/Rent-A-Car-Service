using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RentACar.Infrastructure.Identity;

namespace RentACar.Api.Identity;

/// <summary>
/// LoginResult'tan imzalı JWT üretir. Token, Web cookie'siyle AYNI claim setini taşır
/// (tenant_id/user_id/role/assigned_sube/tenant_code) → API tarafında ApiIdentity bunları
/// okuyup tenant context + yetki + RLS'i besler.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) Issue(LoginResult login)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_o.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, login.User.UserName),
            new(ClaimTypes.Role, login.User.Rol.ToString()),
            new(ApiClaims.UserId, login.User.Id.ToString()),
            new(ApiClaims.TenantId, login.Tenant.Id.ToString()),
            new(ApiClaims.TenantCode, login.Tenant.Code),
            new(ApiClaims.AssignedBranch, login.User.AtanmisSube ?? string.Empty),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _o.Issuer,
            Audience = _o.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expires);
    }
}
