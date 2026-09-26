using System.Net;
using System.Net.Http.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// P0 login brute-force koruması — BAĞIMSIZ ORACLE: limit=3 elle kurulmuş senaryo ⇒ ilk 3 deneme 401
/// (kimlik hatası, limit DEĞİL), 4. deneme 429 + Retry-After; pencere (2 sn) dolunca yeniden 401
/// (yani tekrar değerlendiriliyor). Gerçek HTTP pipeline (ApiFactory) üzerinden.
/// </summary>
[Collection("postgres")]
public sealed class LoginRateLimitTests(PostgresFixture fx)
{
    private static object WrongCredentials => new { firma = "olmayan", kullanici = "kimse", sifre = "yanlis" };

    [Fact]
    public async Task Limit_asilinca_429_ve_pencere_sonrasi_tekrar_degerlendirilir()
    {
        using var api = new ApiFactory(fx.AppConnectionString, new Dictionary<string, string?>
        {
            ["RateLimit:LoginPermit"] = "3",
            ["RateLimit:LoginWindowSeconds"] = "2",
        });
        var c = api.CreateClient();

        for (var i = 1; i <= 3; i++)
        {
            var r = await c.PostAsJsonAsync("/api/v1/auth/login", WrongCredentials);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode); // deneme 1-3: kimlik hatası (limit değil)
        }

        var blocked = await c.PostAsJsonAsync("/api/v1/auth/login", WrongCredentials);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode); // 4. deneme: 429
        Assert.True(blocked.Headers.Contains("Retry-After"));

        await Task.Delay(TimeSpan.FromSeconds(2.5)); // pencere geçsin
        var again = await c.PostAsJsonAsync("/api/v1/auth/login", WrongCredentials);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode); // kalıcı blok değil, pencere bazlı
    }

    [Fact]
    public async Task Dogru_kimlik_limit_icinde_girebilir()
    {
        using var api = new ApiFactory(fx.AppConnectionString, new Dictionary<string, string?>
        {
            ["RateLimit:LoginPermit"] = "3",
            ["RateLimit:LoginWindowSeconds"] = "60",
        });
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, "rlten", "rluser", "rl-sifre-1");

        var c = api.CreateClient();
        var fail = await c.PostAsJsonAsync("/api/v1/auth/login",
            new { firma = "rlten", kullanici = "rluser", sifre = "yanlis" });
        Assert.Equal(HttpStatusCode.Unauthorized, fail.StatusCode); // 1 başarısız deneme limiti tüketmez

        var ok = await c.PostAsJsonAsync("/api/v1/auth/login",
            new { firma = "rlten", kullanici = "rluser", sifre = "rl-sifre-1" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode); // 2. istek: başarılı giriş
    }

    [Fact]
    public async Task Login_disi_uclar_limitlenmez()
    {
        using var api = new ApiFactory(fx.AppConnectionString, new Dictionary<string, string?>
        {
            ["RateLimit:LoginPermit"] = "1",
            ["RateLimit:LoginWindowSeconds"] = "60",
        });
        var c = api.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            var r = await c.GetAsync("/health"); // policy yalnız login'de; health 5 kez serbest
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }
}
