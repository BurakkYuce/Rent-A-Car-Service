using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using Settings = RentACar.Domain.Entities.TenantSettings;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ITenantSettingsRepository: tenant başına TEK ayar satırı. Tenant izolasyonu query filter + RLS ile
/// otomatik. Upsert: yoksa oluştur (TenantId interceptor damgalar), varsa güncelle; eşzamanlı çift insert
/// unique index (TenantId) ile engellenir (23505 → ValidationException).
/// </summary>
public sealed class TenantSettingsRepository(IDbContextFactory<AppDbContext> factory) : ITenantSettingsRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<Settings?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(Action<Settings> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.TenantSettings.FirstOrDefaultAsync(ct);
        var isNew = s is null;
        s ??= new Settings();
        apply(s);
        s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (isNew) db.TenantSettings.Add(s);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException("Ayarlar zaten kayıtlı (eşzamanlı yazım).");
        }
    }
}
