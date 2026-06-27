using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// RentACar.Api gerçek HTTP entegrasyonu (WebApplicationFactory + gerçek PostgreSQL). Güvenlik
/// çekirdeği: JWT login, auth zorunluluğu, JWT'den tenant İZOLASYONU (RLS), rol zorlaması (403),
/// hata zarfı. Her test benzersiz tenant kodu tohumlar (paylaşılan test DB'sinde çakışmaz).
/// </summary>
[Collection("postgres")]
public sealed class ApiTests(PostgresFixture fx)
{
    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}";

    private sealed record LoginBody(string token, DateTimeOffset expiresAt, string tenantCode, string userName, string role);
    private sealed record VehicleBody(Guid id, string plaka, string? grup, string durum, int km, string yakit);
    private sealed record ErrBody(string error, string message);

    private static async Task<HttpClient> LoginAsync(ApiFactory api, string firma, string user, string sifre)
    {
        var c = api.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/v1/auth/login", new { firma, kullanici = user, sifre });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.token);
        return c;
    }

    [Fact]
    public async Task Login_valid_issues_token_and_invalid_is_401()
    {
        var code = Uniq("auth");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p4ss");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = api.CreateClient();

        var ok = await c.PostAsJsonAsync("/api/v1/auth/login", new { firma = code, kullanici = "umit", sifre = "p4ss" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<LoginBody>();
        Assert.False(string.IsNullOrWhiteSpace(body!.token));
        Assert.Equal(code, body.tenantCode);

        var bad = await c.PostAsJsonAsync("/api/v1/auth/login", new { firma = code, kullanici = "umit", sifre = "WRONG" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_requires_token()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = api.CreateClient();
        var resp = await c.GetAsync("/api/v1/vehicles");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Vehicles_are_tenant_isolated_via_jwt()
    {
        var codeA = Uniq("ta");
        var codeB = Uniq("tb");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeA, "umit", "p");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeB, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);

        var ca = await LoginAsync(api, codeA, "umit", "p");
        var created = await ca.PostAsJsonAsync("/api/v1/vehicles",
            new { plaka = "34ISOA1", durum = "Musait", km = 0, yakit = "Benzin" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var aList = await ca.GetFromJsonAsync<List<VehicleBody>>("/api/v1/vehicles");
        Assert.Contains(aList!, v => v.plaka == "34ISOA1");

        // Tenant B, A'nın aracını GÖRMEMELİ (JWT tenant → RLS).
        var cb = await LoginAsync(api, codeB, "umit", "p");
        var bList = await cb.GetFromJsonAsync<List<VehicleBody>>("/api/v1/vehicles");
        Assert.DoesNotContain(bList!, v => v.plaka == "34ISOA1");
    }

    [Fact]
    public async Task Muhasebe_is_forbidden_from_writing_vehicles()
    {
        var code = Uniq("role");
        // Muhasebe: FinanceWrite var, OperationsWrite YOK → araç yazamaz (endpoint 403).
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "muh", "p", UserRole.Muhasebe);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await LoginAsync(api, code, "muh", "p");

        var resp = await c.PostAsJsonAsync("/api/v1/vehicles",
            new { plaka = "34NOPE", durum = "Stokta", km = 0, yakit = "Benzin" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_error_envelope()
    {
        var code = Uniq("err");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await LoginAsync(api, code, "umit", "p");

        var resp = await c.PostAsJsonAsync("/api/v1/vehicles",
            new { plaka = "", durum = "Stokta", km = 0, yakit = "Benzin" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrBody>();
        Assert.Equal("validation", err!.error);
        Assert.False(string.IsNullOrWhiteSpace(err.message));
    }
}
