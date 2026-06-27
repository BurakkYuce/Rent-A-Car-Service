namespace RentACar.Api.Dtos;

/// <summary>İki aşamalı login isteği (firma + kullanıcı + şifre).</summary>
public sealed class LoginRequest
{
    public string Firma { get; set; } = string.Empty;
    public string Kullanici { get; set; } = string.Empty;
    public string Sifre { get; set; } = string.Empty;
}

/// <summary>Login yanıtı: bearer token + son kullanma + kimlik özeti.</summary>
public sealed record LoginResponse(
    string Token, DateTimeOffset ExpiresAt, string TenantCode, string UserName, string Role);
