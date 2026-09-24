using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
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

    public async Task<TenantDomain> AddCustomAsync(Guid tenantId, string host, CancellationToken ct = default)
    {
        host = host.Trim().ToLowerInvariant();
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Aynı host üzerindeki eşzamanlı eklemeler sıraya girer (platform geneli, host başına kilit).
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["tenant-domain:" + host], ct);

            var row = await db.TenantDomains.FirstOrDefaultAsync(d => d.Host == host, ct);
            if (row is not null && row.TenantId == tenantId)
            {
                // Idempotent: kendi satırı. Süresi dolmuş/başarısız bekleyen kayıt yeni belirteçle yeniden başlar.
                if (row.Kind == TenantDomainKind.Custom && (row.Status == TenantDomainStatus.Failed
                        || (row.Status == TenantDomainStatus.PendingVerification && DomainVerification.IsExpired(row.CreatedAtUtc, DateTimeOffset.UtcNow))))
                {
                    await RequirePendingQuotaAsync(db, tenantId, row.Id, ct);
                    row.Status = TenantDomainStatus.PendingVerification;
                    row.VerificationToken = DomainVerification.NewToken();
                    row.CreatedAtUtc = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
                return row;
            }
            if (row is not null)
            {
                // F11.1b güvenlik M6: başka kiracının DOĞRULANMIŞ alan adı alınamaz; doğrulanmamış (bekleyen/başarısız)
                // kayıt ise sahiplik kanıtı değildir ve gerçek sahibin eklemesini ENGELLEMEZ. Mesaj, başka kiracının
                // varlığını ya da durumunu sızdırmaz.
                if (row.Status == TenantDomainStatus.Active || row.Kind != TenantDomainKind.Custom)
                    throw new ValidationException(DomainVerification.CannotAddMessage);
                db.TenantDomains.Remove(row);
                await db.SaveChangesAsync(ct);
            }

            await RequirePendingQuotaAsync(db, tenantId, null, ct);
            var domain = new TenantDomain
            {
                TenantId = tenantId,
                Host = host,
                Kind = TenantDomainKind.Custom,
                Status = TenantDomainStatus.PendingVerification,
                VerificationToken = DomainVerification.NewToken(),
            };
            db.TenantDomains.Add(domain);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return domain;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException(DomainVerification.CannotAddMessage);
        }
    }

    private static async Task RequirePendingQuotaAsync(AppDbContext db, Guid tenantId, Guid? excludeId, CancellationToken ct)
    {
        var openPending = await db.TenantDomains.AsNoTracking().CountAsync(d =>
            d.TenantId == tenantId && d.Kind == TenantDomainKind.Custom && d.Status == TenantDomainStatus.PendingVerification
            && (excludeId == null || d.Id != excludeId), ct);
        if (openPending >= 2)
            throw new ValidationException("Aynı anda en fazla 2 doğrulama bekleyen özel domain ekleyebilirsiniz.");
    }

    /// <summary>F11.1b güvenlik M6 — kiracının kendi bekleyen alan adı kaydı (yoksa null).</summary>
    public async Task<TenantDomain?> FindCustomAsync(Guid tenantId, string host, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var h = host.Trim().ToLowerInvariant();
        return await db.TenantDomains.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Host == h && d.Kind == TenantDomainKind.Custom, ct);
    }

    /// <summary>F11.1b güvenlik M6 — belirteç doğrulandıktan SONRA etkinleştirme; yalnız aynı kiracı, aynı belirteç ve
    /// hâlâ bekleyen satır (arada yeniden eklenip belirteç değiştiyse etkinleşmez).</summary>
    public async Task<bool> ActivateVerifiedAsync(Guid tenantId, Guid id, string token, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TenantDomains
            .Where(d => d.Id == id && d.TenantId == tenantId && d.Status == TenantDomainStatus.PendingVerification
                        && d.VerificationToken == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, TenantDomainStatus.Active)
                .SetProperty(d => d.VerifiedAtUtc, DateTimeOffset.UtcNow), ct) > 0;
    }

    public async Task<bool> ExistsAsync(string host, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var h = host.Trim().ToLowerInvariant();
        return await db.TenantDomains.AsNoTracking().AnyAsync(d => d.Host == h, ct);
    }

    public async Task MarkVerifiedAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.TenantDomains.Where(d => d.Id == id && d.Status == TenantDomainStatus.PendingVerification)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, TenantDomainStatus.Active)
                .SetProperty(d => d.VerifiedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    public async Task<IReadOnlyList<TenantDomain>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TenantDomains.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Kind).ThenBy(d => d.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<int> ExpireOldPendingCustomDomainsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TenantDomains
            .Where(d => d.Status == TenantDomainStatus.PendingVerification && d.CreatedAtUtc < olderThan)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, TenantDomainStatus.Failed), ct);
    }
}
