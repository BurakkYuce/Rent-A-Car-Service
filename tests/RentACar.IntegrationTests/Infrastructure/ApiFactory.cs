using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// RentACar.Api'yi bellek-içi host eder (gerçek HTTP pipeline). Default bağlantı + JWT
/// ayarları test DB'sine yönlendirilir. API JWT'den tenant context çözer → RLS test DB'de
/// gerçekten uygulanır (izolasyon ispatlanabilir).
/// </summary>
public sealed class ApiFactory(string appConnectionString) : WebApplicationFactory<RentACar.Api.ApiAssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", appConnectionString);
        builder.UseSetting("Jwt:Key", "test-only-symmetric-key-at-least-32-bytes-long!!");
        builder.UseSetting("Jwt:Issuer", "RentACarApi");
        builder.UseSetting("Jwt:Audience", "RentACarClients");
        builder.UseSetting("Jwt:ExpiresMinutes", "60");
    }
}

/// <summary>API testleri için tenant + kullanıcı tohumlama (owner bağlantısı; platform tabloları).</summary>
public static class ApiSeed
{
    public static async Task<Guid> TenantUserAsync(
        string ownerConn, string code, string userName, string password,
        UserRole role = UserRole.Admin, string? branch = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ownerConn).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);

        var tenant = new Tenant { Code = code, Name = code, IsActive = true };
        db.Tenants.Add(tenant);

        var user = new User
        {
            TenantId = tenant.Id, UserName = userName, DisplayName = userName,
            Rol = role, AtanmisSube = branch, IsActive = true
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);

        await db.SaveChangesAsync();
        return tenant.Id;
    }
}

/// <summary>API testleri için login → Bearer header'lı HttpClient.</summary>
public static class ApiClientExtensions
{
    public static async Task<HttpClient> LoginClientAsync(this ApiFactory api, string firma, string user, string sifre)
    {
        var c = api.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/v1/auth/login", new { firma, kullanici = user, sifre });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}
