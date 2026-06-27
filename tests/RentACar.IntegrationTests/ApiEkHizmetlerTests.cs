using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>Ek hizmet master API — gerçek HTTP: CRUD roundtrip + tenant izolasyonu (JWT→RLS).</summary>
[Collection("postgres")]
public sealed class ApiEkHizmetlerTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";
    private sealed record Body(Guid id, string kod, string ad, decimal birimUcret, decimal kdvOrani);

    [Fact]
    public async Task Create_list_roundtrip_and_isolation()
    {
        var codeA = Uniq("eha");
        var codeB = Uniq("ehb");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeA, "umit", "p");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeB, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);

        var ca = await api.LoginClientAsync(codeA, "umit", "p");
        var created = await ca.PostAsJsonAsync("/api/v1/ek-hizmetler",
            new { kod = "gps", ad = "Navigasyon", birimUcret = 50m, kdvOrani = 0.20m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<Body>();
        Assert.Equal("GPS", body!.kod); // normalize

        var list = await ca.GetFromJsonAsync<List<Body>>("/api/v1/ek-hizmetler");
        Assert.Contains(list!, x => x.kod == "GPS");

        // Tenant B görmemeli.
        var cb = await api.LoginClientAsync(codeB, "umit", "p");
        var bList = await cb.GetFromJsonAsync<List<Body>>("/api/v1/ek-hizmetler");
        Assert.DoesNotContain(bList!, x => x.kod == "GPS");
    }
}
