using Microsoft.EntityFrameworkCore;
using RentACar.Application.Fleet;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IPublicBrandingRepository implementasyonu (PR-4). Tenants (platform, isimsiz fallback) +
/// TenantSettings (ITenantOwned/RLS'li — normal DI-scoped context zaten GUC'u ayarlamış oluyor) tek
/// sorguda birleşir.</summary>
public sealed class PublicBrandingRepository(IDbContextFactory<AppDbContext> factory) : IPublicBrandingRepository
{
    public async Task<FleetBranding> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var tenantName = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);
        var s = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var marka = string.IsNullOrWhiteSpace(s?.FirmaMarka) ? tenantName : s.FirmaMarka;
        return new FleetBranding(marka, s?.FirmaAdres, s?.FirmaTel, s?.FirmaEmail);
    }
}
