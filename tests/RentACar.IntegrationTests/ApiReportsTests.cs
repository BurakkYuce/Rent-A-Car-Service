using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Raporlar API (salt-okunur). ViewReports yetki kapısı (Admin 200 / Operatör 403 / token yok 401)
/// + JSON şekil. Rapor DOĞRULUĞU servis-katmanı ReportingTests'te (oracle) kapsanır.
/// </summary>
[Collection("postgres")]
public sealed class ApiReportsTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";

    [Fact]
    public async Task Reports_accessible_with_viewreports_and_shape_ok()
    {
        var code = Uniq("rep");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p", UserRole.Admin);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        var filo = await c.GetAsync("/api/v1/reports/filo");
        Assert.Equal(HttpStatusCode.OK, filo.StatusCode);
        var filoBody = await filo.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(filoBody.TryGetProperty("toplam", out _));

        var kasa = await c.GetAsync("/api/v1/reports/kasa-banka");
        Assert.Equal(HttpStatusCode.OK, kasa.StatusCode);
        var kasaBody = await kasa.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(kasaBody.TryGetProperty("kasaBakiye", out _));
    }

    [Fact]
    public async Task Reports_forbidden_for_operator_role()
    {
        var code = Uniq("repop");
        // Operatör: OperationsWrite var, ViewReports YOK → 403.
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "op", "p", UserRole.Operator);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "op", "p");

        var resp = await c.GetAsync("/api/v1/reports/filo");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Reports_require_token()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = api.CreateClient();
        var resp = await c.GetAsync("/api/v1/reports/filo");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
