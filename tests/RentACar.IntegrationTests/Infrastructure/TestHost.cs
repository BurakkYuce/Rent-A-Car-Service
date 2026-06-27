using Microsoft.Extensions.DependencyInjection;
using RentACar.Application;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>Tenant/kullanıcıyı test içinde set edilebilen kimlik double'ı.</summary>
public sealed class TestIdentity : ITenantContext, ICurrentUser
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public UserRole? Role { get; set; }
}

/// <summary>Test parola özetleyici (gerçek kripto gerekmez; sadece tutar/doğrular).</summary>
public sealed class TestPasswordHasher : RentACar.Application.Common.IPasswordHasher
{
    public string Hash(string password) => "hash:" + password;
    public bool Verify(string hash, string password) => hash == "hash:" + password;
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
        services.AddSingleton<RentACar.Application.Common.IPasswordHasher, TestPasswordHasher>();
        services.AddApplication();
        services.AddInfrastructure(appConnectionString);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Verilen tenant/kullanıcı(/rol) için yeni bir DI scope'u açar. Rol VARSAYILANI Admin'dir:
    /// çoğu test "yetkili kullanıcı X yapar" senaryosunu kurar; yetki REDDİ testleri rolü açıkça
    /// kısıtlar (ör. Operator).
    /// </summary>
    public IServiceScope ScopeFor(
        Guid? tenantId, Guid? userId = null, string? userName = "tester", UserRole? role = UserRole.Admin)
    {
        var scope = _provider.CreateScope();
        var id = scope.ServiceProvider.GetRequiredService<TestIdentity>();
        id.TenantId = tenantId;
        id.UserId = userId;
        id.UserName = userName;
        id.Role = role;
        return scope;
    }

    public void Dispose() => _provider.Dispose();
}
