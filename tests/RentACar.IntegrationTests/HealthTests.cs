using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// P0-2 sağlık ucu — BAĞIMSIZ ORACLE: DB erişilebilirken /health 200 "healthy";
/// DB erişilemezken (dinlemeyen port) 503 "unhealthy". Uptime monitörünün göreceği sözleşme.
/// </summary>
[Collection("postgres")]
public sealed class HealthTests(PostgresFixture fx)
{
    [Fact]
    public async Task Db_ayaktayken_200_healthy()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var r = await api.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("healthy", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Db_erisilemezken_503_unhealthy()
    {
        // 59999: dinleyen süreç yok → bağlantı hızla reddedilir (Timeout=1 emniyet).
        using var api = new ApiFactory(
            "Host=localhost;Port=59999;Username=yok;Password=yok;Database=yok;Timeout=1");
        var r = await api.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Contains("unhealthy", await r.Content.ReadAsStringAsync());
    }
}
