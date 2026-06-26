using Microsoft.Extensions.DependencyInjection;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Infrastructure;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>Tenant/kullanıcıyı test içinde set edilebilen kimlik double'ı.</summary>
public sealed class TestIdentity : ITenantContext, ICurrentUser
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
}

/// <summary>
/// Gerçek DI grafiğini (Application + Infrastructure) racar_app bağlantısıyla kurar.
/// Belirli bir tenant için scope açar → o scope'taki factory/servis/interceptor'lar
/// o tenant'la (ve RLS GUC'uyla) çalışır.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestHost(string appConnectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<TestIdentity>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TestIdentity>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestIdentity>());
        services.AddApplication();
        services.AddInfrastructure(appConnectionString);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>Verilen tenant/kullanıcı için yeni bir DI scope'u açar.</summary>
    public IServiceScope ScopeFor(Guid? tenantId, Guid? userId = null, string? userName = "tester")
    {
        var scope = _provider.CreateScope();
        var id = scope.ServiceProvider.GetRequiredService<TestIdentity>();
        id.TenantId = tenantId;
        id.UserId = userId;
        id.UserName = userName;
        return scope;
    }

    public void Dispose() => _provider.Dispose();
}
