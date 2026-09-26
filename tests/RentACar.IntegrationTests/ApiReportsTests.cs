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

        var fleet = await c.GetAsync("/api/v1/reports/filo");
        Assert.Equal(HttpStatusCode.OK, fleet.StatusCode);
        var fleetBody = await fleet.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(fleetBody.TryGetProperty("toplam", out _));

        var cash = await c.GetAsync("/api/v1/reports/kasa-banka");
        Assert.Equal(HttpStatusCode.OK, cash.StatusCode);
        var cashBody = await cash.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(cashBody.TryGetProperty("kasaBakiye", out _));
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
