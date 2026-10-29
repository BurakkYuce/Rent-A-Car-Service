using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Gerçek (kiracıda VAR olan) test aracı. <c>RentalService.CreateDirectAsync</c> müşteri/araç varlığını girişte
/// doğruluyor (DEVIR §6 Low) — kira testleri uydurma araç kimliği kullanamaz. Plaka benzersiz üretilir.
/// </summary>
public static class TestVehicle
{
    /// <summary>Verilen kapsamda (yetkili rol) yeni araç.</summary>
    public static Task<Guid> NewAsync(IServiceProvider sp, string? plate = null)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate ?? RandomPlate() });

    /// <summary>Kiracıda Admin kapsamında yeni araç (çağıranın kapsamı araç açamayan bir rolse).</summary>
    public static async Task<Guid> NewAsync(TestHost host, Guid tenant, string? plate = null)
    {
        using var s = host.ScopeFor(tenant, role: UserRole.Admin);
        return await NewAsync(s.ServiceProvider, plate);
    }

    // Rastgele başlangıç + süreç içi sayaç: aynı kiracıda iki plaka asla çakışmaz (salt rastgele 90 bin aralıkta
    // CI'da çakıştı: "34T19610 zaten kayıtlı"). Başlangıç rastgele kalır ki ayrı koşular farklı plakalar görsün.
    private static int _next = Random.Shared.Next(10000, 60000);

    private static string RandomPlate() => "34T" + Interlocked.Increment(ref _next);
}
