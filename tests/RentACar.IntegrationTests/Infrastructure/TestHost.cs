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
    public string? AssignedBranch { get; set; }
    public Guid? AssignedBranchId { get; set; } // FAZ 5-C1 (testler C2+ ile FK-kapsamı da kurar)

    /// <summary>PR-4.5: varsayılan false — mevcut default-deny testleri (TenantIsolationTests) etkilenmez.
    /// Yalnız PublicTenantContext'in ThrowIfTenantMissing=true davranışını taklit eden testler set eder.</summary>
    public bool ThrowIfTenantMissing { get; set; }

    /// <summary>Kullanıcı-bazlı izin istisnaları (2026-08-17). Varsayılan boş — istisna testleri
    /// claim'e yazılmış hâli taklit etmek için doldurur (web'de login claim'inden gelir).</summary>
    public IReadOnlyCollection<string> EkIzinler { get; set; } = [];
    public IReadOnlyCollection<string> YasakIzinler { get; set; } = [];
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

    /// <param name="extra">Gerçek kayıtlardan SONRA çalışan ek kayıtlar — testin bir portu sahte
    /// uygulamayla değiştirmesine izin verir (ör. e-posta göndericisi; gerçek SMTP'ye çıkmayalım).
    /// Varsayılan null → mevcut testler birebir aynı grafiği kurar.</param>
    public TestHost(string appConnectionString, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // LoginService gibi ILogger isteyen gerçek servisler için (no-op sink)
        services.AddScoped<TestIdentity>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TestIdentity>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestIdentity>());
        services.AddSingleton<RentACar.Application.Common.IPasswordHasher, TestPasswordHasher>();
        services.AddApplication();
        services.AddInfrastructure(appConnectionString);
        extra?.Invoke(services); // son kayıt kazanır → test double'ı gerçek uygulamayı override eder
        _provider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Verilen tenant/kullanıcı(/rol) için yeni bir DI scope'u açar. Rol VARSAYILANI Admin'dir:
    /// çoğu test "yetkili kullanıcı X yapar" senaryosunu kurar; yetki REDDİ testleri rolü açıkça
    /// kısıtlar (ör. Operator).
    /// </summary>
    public IServiceScope ScopeFor(
        Guid? tenantId, Guid? userId = null, string? userName = "tester",
        UserRole? role = UserRole.Admin, string? assignedBranch = null, Guid? assignedBranchId = null)
    {
        var scope = _provider.CreateScope();
        var id = scope.ServiceProvider.GetRequiredService<TestIdentity>();
        id.TenantId = tenantId;
        id.UserId = userId;
        id.UserName = userName;
        id.Role = role;
        id.AssignedBranch = assignedBranch;
        id.AssignedBranchId = assignedBranchId; // FAZ 5-C2
        return scope;
    }

    public void Dispose() => _provider.Dispose();
}
