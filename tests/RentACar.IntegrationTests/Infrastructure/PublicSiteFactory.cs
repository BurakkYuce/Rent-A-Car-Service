using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// RentACar.PublicSite'ı GERÇEK Program.cs boru hattıyla bellek-içi host eder (ara katman sırası,
/// rate limiter, yeniden çalıştırma, Razor sayfaları dahil). Giriş noktası türü olarak derlemedeki
/// herhangi bir tür yeter; <c>Program</c> kullanılmaz çünkü test projesi Web'i de referans alıyor ve
/// iki üst-düzey <c>Program</c> çakışır.
///
/// <para>Host → tenant çözümlemesi sabit bir tenant'a bağlanır (<see cref="FixedTenantResolver"/>):
/// TestServer'ın Host'u "localhost" ve bu testlerin konusu çözümleme değil.</para>
/// </summary>
public sealed class PublicSiteFactory(PostgresFixture pg, Guid tenantId, int searchPermit = 10_000,
    int bookingPermit = 10_000)
    : WebApplicationFactory<NoIndexHeaderMiddleware>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", pg.AppConnectionString);
        builder.UseSetting("RateLimit:PublicSearchPermit", searchPermit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimit:BookingRequestPermit", bookingPermit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(s =>
        {
            var jobs = s.Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType?.Namespace?.StartsWith("RentACar", StringComparison.Ordinal) == true)
                         .ToList();
            foreach (var d in jobs) s.Remove(d);
            s.AddSingleton<IPublicTenantResolver>(new FixedTenantResolver(tenantId));
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private sealed class FixedTenantResolver(Guid tenantId) : IPublicTenantResolver
    {
        public Task<PublicTenantResult> ResolveAsync(string host, CancellationToken ct = default)
            => Task.FromResult(new PublicTenantResult(PublicTenantResolution.Found, tenantId));
    }
}
