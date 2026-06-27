using System.Net;
using System.Net.Http.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Cari (Customers) API — gerçek HTTP. CRUD roundtrip, tenant izolasyonu (JWT→RLS), benzersizlik
/// çakışması (TC) → 409 hata zarfı. Auth/rol/401 çekirdeği ApiTests'te (Vehicles) kapsanır.
/// </summary>
[Collection("postgres")]
public sealed class ApiCustomersTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";

    private sealed record CustomerBody(Guid id, string tip, string displayName, string? tcKimlik);
    private sealed record ErrBody(string error, string message);

    [Fact]
    public async Task Create_then_get_roundtrip()
    {
        var code = Uniq("cust");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        var created = await c.PostAsJsonAsync("/api/v1/customers",
            new { tip = "Bireysel", ad = "Ahmet", soyad = "Yılmaz" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<CustomerBody>();
        Assert.Equal("Ahmet Yılmaz", body!.displayName);

        var fetched = await c.GetFromJsonAsync<CustomerBody>($"/api/v1/customers/{body.id}");
        Assert.Equal(body.id, fetched!.id);
    }

    [Fact]
    public async Task Customers_are_tenant_isolated()
    {
        var codeA = Uniq("ca");
        var codeB = Uniq("cb");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeA, "umit", "p");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeB, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);

        var ca = await api.LoginClientAsync(codeA, "umit", "p");
        await ca.PostAsJsonAsync("/api/v1/customers", new { tip = "Kurumsal", unvan = "ACME A.Ş." });

        var cb = await api.LoginClientAsync(codeB, "umit", "p");
        var bList = await cb.GetFromJsonAsync<List<CustomerBody>>("/api/v1/customers");
        Assert.DoesNotContain(bList!, c => c.displayName == "ACME A.Ş.");
    }

    [Fact]
    public async Task Duplicate_tc_returns_409_conflict()
    {
        var code = Uniq("dup");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        var tc = "10000000146"; // geçerli checksum'lı TC
        var first = await c.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "A", tcKimlik = tc });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await c.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "B", tcKimlik = tc });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var err = await second.Content.ReadFromJsonAsync<ErrBody>();
        Assert.Equal("duplicate", err!.error);
    }
}
