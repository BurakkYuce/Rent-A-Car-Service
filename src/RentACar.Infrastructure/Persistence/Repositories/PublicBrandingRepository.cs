using Microsoft.EntityFrameworkCore;
using RentACar.Application.Fleet;
using RentACar.Domain.Entities;

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

    public async Task<string?> GetCanonicalHostAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // TenantDomains PLATFORM tablosu (RLS yok) → tenantId ile AÇIKÇA filtrelenir.
        var aktifler = await db.TenantDomains.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Status == TenantDomainStatus.Active)
            .Select(d => new { d.Host, d.Kind, d.VerifiedAtUtc, d.CreatedAtUtc })
            .ToListAsync(ct);

        // Deterministik: Active Custom'lar arasında EN ESKİ doğrulanan kazanır (VerifiedAtUtc null ise
        // CreatedAtUtc'ye düşer — sıralama her koşulda tanımlı). Custom yoksa subdomain.
        var custom = aktifler
            .Where(d => d.Kind == TenantDomainKind.Custom)
            .OrderBy(d => d.VerifiedAtUtc ?? d.CreatedAtUtc)
            .Select(d => d.Host)
            .FirstOrDefault();
        if (custom is not null) return custom;

        return aktifler.Where(d => d.Kind == TenantDomainKind.Subdomain)
            .OrderBy(d => d.CreatedAtUtc).Select(d => d.Host).FirstOrDefault();
    }
}
