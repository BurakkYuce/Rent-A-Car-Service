using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Güvenlik tur 2 L2 — JWT (<c>/api/v1</c>) belirteci oturum damgasını taşır; <c>OnTokenValidated</c> damga değişince
/// ya da kullanıcı pasifleşince ZATEN VERİLMİŞ belirteci reddeder (401). Yeni giriş yeni damgayla çalışır.
/// <para>Diğer <see cref="ApiFactory"/> testleriyle aynı ("postgres") koleksiyonda. İlk sürümü "web" koleksiyonundaydı
/// ve Api host'u Web'in Serilog statik logger'ını kapatıyordu; kök neden <see cref="ApiFactory"/>'de giderildi
/// (<c>ApiFactoryLogIsolationTests</c> kilitler).</para>
/// </summary>
[Collection("postgres")]
public sealed class ApiSessionStampTests(PostgresFixture fx)
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task L2_JWT_damga_ya_da_aktiflik_degisince_belirtec_reddedilir()
    {
        var password = WebFixture.RandomPassword();
        var opCompany = Unique("stp");
        var accCompany = Unique("stp");
        var opTenant = await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, opCompany, "op", password, UserRole.Operator, "SubeA");
        var accTenant = await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, accCompany, "acc", password, UserRole.Muhasebe);

        using var api = new ApiFactory(fx.AppConnectionString);
        var op = await api.LoginClientAsync(opCompany, "op", password);
        var acc = await api.LoginClientAsync(accCompany, "acc", password);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/v1/legal")).StatusCode);

        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            (await db.Users.SingleAsync(x => x.TenantId == opTenant && x.UserName == "op")).GuvenlikDamgasi = Guid.NewGuid().ToString("N");
            (await db.Users.SingleAsync(x => x.TenantId == accTenant && x.UserName == "acc")).IsActive = false;
            await db.SaveChangesAsync();
        }
        // Damga önbelleği (en çok 10 sn) bu süreçte yazılmadığı için bayat olabilir: değişiklik pencere içinde görünmeli.
        await AssertEventuallyUnauthorized(op, "/api/v1/legal");
        await AssertEventuallyUnauthorized(acc, "/api/v1/donem-kapanis");

        // Yeni giriş (yeni damga) çalışır.
        var op2 = await api.LoginClientAsync(opCompany, "op", password);
        Assert.Equal(HttpStatusCode.OK, (await op2.GetAsync("/api/v1/legal")).StatusCode);
    }

    private static async Task AssertEventuallyUnauthorized(HttpClient c, string url)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        HttpStatusCode last;
        do
        {
            last = (await c.GetAsync(url)).StatusCode;
            if (last == HttpStatusCode.Unauthorized) return;
            await Task.Delay(250);
        } while (DateTime.UtcNow < end);
        Assert.Fail($"{url}: belirteç hâlâ geçerli ({(int)last})");
    }
}
