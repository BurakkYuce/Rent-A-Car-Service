using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Finans API — gerçek HTTP. Tahsilat cari bakiyeyi düşürür, ters kayıt geri alır (oracle);
/// FinanceWrite yetki kapısı (Operatör 403); tenant izolasyonu (JWT→RLS, B, A'nın defterini
/// görmez). Para mantığı CashService'te (adversarial-incelenmiş); bu testler API güvenlik +
/// doğru bağlama odaklı.
/// </summary>
[Collection("postgres")]
public sealed class ApiFinanceTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";

    private static async Task<Guid> CreateIdAsync(HttpClient c, string url, object body)
    {
        var resp = await c.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<decimal> BalanceAsync(HttpClient c, Guid cariId)
    {
        var body = await c.GetFromJsonAsync<JsonElement>($"/api/v1/finance/customers/{cariId}/balance");
        return body.GetProperty("bakiye").GetDecimal();
    }

    [Fact]
    public async Task Collect_lowers_cari_balance_and_reverse_restores()
    {
        var code = Uniq("fin");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p", UserRole.Admin);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        var cari = await CreateIdAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Fin", soyad = "Test" });
        Assert.Equal(0m, await BalanceAsync(c, cari));

        // Tahsilat 1000 (Borç Kasa / Alacak Cari) → cari bakiye −1000.
        var cashId = await CreateIdAsync(c, "/api/v1/finance/cash/collect",
            new { cariId = cari, tutar = 1000m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(-1000m, await BalanceAsync(c, cari));

        // Ters kayıt → bakiye 0.
        var rev = await c.PostAsync($"/api/v1/finance/cash/{cashId}/reverse", null);
        Assert.Equal(HttpStatusCode.Created, rev.StatusCode);
        Assert.Equal(0m, await BalanceAsync(c, cari));
    }

    [Fact]
    public async Task Finance_forbidden_for_operator_role()
    {
        var code = Uniq("finop");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "op", "p", UserRole.Operator);
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "op", "p");

        var resp = await c.GetAsync("/api/v1/finance/cash");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Finance_is_tenant_isolated()
    {
        var codeA = Uniq("fa");
        var codeB = Uniq("fb");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeA, "umit", "p", UserRole.Admin);
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeB, "umit", "p", UserRole.Admin);
        using var api = new ApiFactory(fx.AppConnectionString);

        var ca = await api.LoginClientAsync(codeA, "umit", "p");
        var cari = await CreateIdAsync(ca, "/api/v1/customers", new { tip = "Bireysel", ad = "A" });
        await CreateIdAsync(ca, "/api/v1/finance/cash/collect",
            new { cariId = cari, tutar = 500m, doviz = "TRY", kur = 1m, hesap = "Kasa" });

        // Tenant B: A'nın nakit listesi görünmez; A'nın carisinin bakiyesi B'ye 0.
        var cb = await api.LoginClientAsync(codeB, "umit", "p");
        var bCash = await cb.GetFromJsonAsync<List<JsonElement>>("/api/v1/finance/cash");
        Assert.Empty(bCash!);
        Assert.Equal(0m, await BalanceAsync(cb, cari));
    }
}
