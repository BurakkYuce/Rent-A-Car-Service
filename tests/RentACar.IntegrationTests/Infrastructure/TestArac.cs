using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Gerçek (kiracıda VAR olan) test aracı. <c>RentalService.CreateDirectAsync</c> müşteri/araç varlığını girişte
/// doğruluyor (DEVIR §6 Low) — kira testleri uydurma araç kimliği kullanamaz. Plaka benzersiz üretilir.
/// </summary>
public static class TestArac
{
    /// <summary>Verilen kapsamda (yetkili rol) yeni araç.</summary>
    public static Task<Guid> YeniAsync(IServiceProvider sp, string? plaka = null)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka ?? RastgelePlaka() });

    /// <summary>Kiracıda Admin kapsamında yeni araç (çağıranın kapsamı araç açamayan bir rolse).</summary>
    public static async Task<Guid> YeniAsync(TestHost host, Guid tenant, string? plaka = null)
    {
        using var s = host.ScopeFor(tenant, role: UserRole.Admin);
        return await YeniAsync(s.ServiceProvider, plaka);
    }

    private static string RastgelePlaka() => "34T" + Random.Shared.Next(10000, 99999);
}
