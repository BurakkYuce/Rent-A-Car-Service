using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RentACar.Api.Common;
using RentACar.Api.Endpoints;
using RentACar.Api.Identity;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Infrastructure;
using RentACar.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var appConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");

// JSON: enum'lar string olarak (durum/yakıt vb.).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Uygulama + altyapı (LoginService + IPasswordHasher<User> AddInfrastructure'da kayıtlı).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(appConn);

// Kimlik: ITenantContext + ICurrentUser → JWT bearer claim'lerinden (tek örnek iki arayüze).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ApiIdentity>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ApiIdentity>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ApiIdentity>());

// JWT üretimi + doğrulama.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<JwtTokenService>();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = signingKey,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

// Tutarlı JSON hata zarfı (en dış katman).
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapOpenApi(); // OpenAPI dokümanı: /openapi/v1.json

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthApi();
app.MapVehiclesApi();
app.MapCustomersApi();
app.MapReservationsApi();
app.MapRentalsApi();
app.MapReportsApi();
app.MapFinanceApi();

// Sağlık (readiness): DB bağlanabiliyor mu? Anonim (ops ping'i).
app.MapGet("/health", async (IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
{
    try
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "healthy" })
            : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous().WithTags("Health");

app.Run();
