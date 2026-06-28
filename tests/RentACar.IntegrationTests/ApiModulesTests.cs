using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Yeni modül API'leri (roadmap E1, salt-okunur): yetki kapısı (doğru rol 200 / yanlış rol 403 / token yok
/// 401) + JSON şekil + personel PII'siz. Veri DOĞRULUĞU servis-katmanı testlerinde (oracle) kapsanır.
/// </summary>
[Collection("postgres")]
public sealed class ApiModulesTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";

    [Fact]
    public async Task Modules_accessible_with_right_role_and_shape()
    {
        var code = Uniq("mod");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p", UserRole.Admin);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        var karlilik = await c.GetAsync("/api/v1/reports/karlilik");
        Assert.Equal(HttpStatusCode.OK, karlilik.StatusCode);
        Assert.True((await karlilik.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("toplamGelir", out _));

        var legal = await c.GetAsync("/api/v1/legal");
        Assert.Equal(HttpStatusCode.OK, legal.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await legal.Content.ReadFromJsonAsync<JsonElement>()).ValueKind);

        var donem = await c.GetAsync("/api/v1/donem-kapanis");
        Assert.Equal(HttpStatusCode.OK, donem.StatusCode);
        Assert.True((await donem.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("kapanisTarihi", out _));

        var personel = await c.GetAsync("/api/v1/personel");
        Assert.Equal(HttpStatusCode.OK, personel.StatusCode);
        var pBody = await personel.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tcKimlik", pBody, StringComparison.OrdinalIgnoreCase); // PII sızmaz
        Assert.DoesNotContain("maas", pBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enc", pBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Personel_requires_manageusers_role()
    {
        var code = Uniq("modop");
        // Operatör: OperationsWrite var, ManageUsers YOK → 403.
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "op", "p", UserRole.Operator);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "op", "p");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/personel")).StatusCode);
        // Operatör hukuk (OperationsWrite) görebilir ama karlilik (ViewReports) göremez.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/legal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/reports/karlilik")).StatusCode);
    }

    [Fact]
    public async Task Modules_require_token()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/personel")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/donem-kapanis")).StatusCode);
    }
}
