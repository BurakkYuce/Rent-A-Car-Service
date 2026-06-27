using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Api.Identity;
using RentACar.Infrastructure.Identity;

namespace RentACar.Api.Endpoints;

/// <summary>İki aşamalı login → JWT bearer token. Anonim (token üretir).</summary>
public static class AuthApi
{
    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/auth").WithTags("Auth");

        grp.MapPost("/login", async (LoginRequest req, LoginService login, JwtTokenService jwt) =>
        {
            var result = await login.ValidateAsync(req.Firma, req.Kullanici, req.Sifre);
            if (result is null)
                return Results.Json(new ApiError("unauthorized", "Firma, kullanıcı veya şifre hatalı."),
                    statusCode: StatusCodes.Status401Unauthorized);

            var (token, expiresAt) = jwt.Issue(result);
            return Results.Ok(new LoginResponse(
                token, expiresAt, result.Tenant.Code, result.User.UserName, result.User.Rol.ToString()));
        }).AllowAnonymous();

        return app;
    }
}
