using Microsoft.EntityFrameworkCore;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>PR-2: TenantDomain platform tablosu (RLS yok — Tenants/Users gibi) CRUD'u.</summary>
public sealed class TenantDomainRepository(IDbContextFactory<AppDbContext> factory) : ITenantDomainRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task EnsureSubdomainAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var exists = await db.TenantDomains.AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Kind == TenantDomainKind.Subdomain, ct);
        if (exists) return; // idempotent — "Sitemi Aç" tekrar tıklanırsa no-op

        var code = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId).Select(t => t.Code).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Tenant bulunamadı — subdomain oluşturulamadı.");

        db.TenantDomains.Add(new TenantDomain
        {
            TenantId = tenantId,
            Host = $"{code}.rentpro.com".ToLowerInvariant(),
            Kind = TenantDomainKind.Subdomain,
            Status = TenantDomainStatus.Active
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetActiveHostAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TenantDomains.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Status == TenantDomainStatus.Active)
            .OrderBy(d => d.Kind) // Subdomain(0) önce
            .Select(d => d.Host)
            .FirstOrDefaultAsync(ct);
    }
}
