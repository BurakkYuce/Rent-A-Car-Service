using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// API olgunluk: sayfalı + filtreli liste uçları (vehicles) ve /health. Sayfalama zarfı
/// {items,total,page,pageSize,totalPages}; /health anonim DB-readiness.
/// </summary>
[Collection("postgres")]
public sealed class ApiPaginationTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";
    private sealed record VehicleBody(Guid id, string plaka);
    private sealed record Paged<T>(List<T> items, int total, int page, int pageSize, int totalPages);

    [Fact]
    public async Task Vehicle_list_paginates_and_filters()
    {
        var code = Uniq("pg");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        foreach (var plk in new[] { "34PG01", "34PG02", "06PG03" })
            await c.PostAsJsonAsync("/api/v1/vehicles", new { plaka = plk, durum = "Musait", km = 0, yakit = "Benzin" });

        // Sayfa 1, boyut 2 → 2 öğe, toplam 3, 2 sayfa.
        var p1 = await c.GetFromJsonAsync<Paged<VehicleBody>>("/api/v1/vehicles?page=1&pageSize=2");
        Assert.Equal(3, p1!.total);
        Assert.Equal(2, p1.items.Count);
        Assert.Equal(2, p1.totalPages);

        // Sayfa 2 → kalan 1 öğe.
        var p2 = await c.GetFromJsonAsync<Paged<VehicleBody>>("/api/v1/vehicles?page=2&pageSize=2");
        Assert.Single(p2!.items);

        // Filtre q=34PG → 2 (34PG01, 34PG02).
        var filtered = await c.GetFromJsonAsync<Paged<VehicleBody>>("/api/v1/vehicles?q=34PG");
        Assert.Equal(2, filtered!.total);
        Assert.All(filtered.items, v => Assert.StartsWith("34PG", v.plaka));
    }

    [Fact]
    public async Task Health_is_anonymous_and_healthy()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = api.CreateClient(); // token YOK

        var resp = await c.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }
}
