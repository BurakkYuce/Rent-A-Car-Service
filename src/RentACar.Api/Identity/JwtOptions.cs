namespace RentACar.Api.Identity;

/// <summary>JWT yapılandırması (appsettings "Jwt" bölümü).</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "RentACarApi";
    public string Audience { get; set; } = "RentACarClients";
    public string Key { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 480;
}
