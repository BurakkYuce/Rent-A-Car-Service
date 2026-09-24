using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using Settings = RentACar.Domain.Entities.TenantSettings;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ITenantSettingsRepository: tenant başına TEK ayar satırı. Tenant izolasyonu query filter + RLS ile
/// otomatik. Upsert: yoksa oluştur (TenantId interceptor damgalar), varsa güncelle; eşzamanlı çift insert
/// unique index (TenantId) ile engellenir (23505 → ValidationException).
/// </summary>
public sealed class TenantSettingsRepository(IDbContextFactory<AppDbContext> factory)
    : ITenantSettingsRepository, ITenantSettingsVersionStore
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    /// <summary>F11.1b — ayar satırının sürümü (xmin); satır yoksa null.</summary>
    public async Task<string?> VersionAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var id = await db.TenantSettings.AsNoTracking().Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return id is { } k ? await SatirSurumu.OkuAsync(db, SatirSurumu.FirmaAyarlari, k, ct) : null;
    }

    /// <summary>
    /// F11.1b — kilit + sürüm karşılaştırması + upsert tek işlemde (<see cref="SatirSurumu"/> deseni). Satır yokken
    /// eşzamanlı iki ilk yazımın kaybedeni unique index'e çarpar → 409 (sessizce üzerine yazmaz).
    /// </summary>
    public async Task UpsertAsync(Action<Settings> apply, string? expectedVersion, CancellationToken ct = default)
    {
        try
        {
            await PgRetry.RunAsync(async () =>
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var id = await db.TenantSettings.AsNoTracking().Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
                if (id is { } k)
                {
                    await SatirSurumu.KilitleAsync(db, SatirSurumu.FirmaAyarlari, k, ct);
                    var current = await SatirSurumu.OkuAsync(db, SatirSurumu.FirmaAyarlari, k, ct);
                    if (expectedVersion is null || !string.Equals(current, expectedVersion.Trim(), StringComparison.Ordinal))
                        throw new EszamanliDegisiklikException(EszamanliDegisiklikException.KayitMesaji);
                }
                else if (expectedVersion is not null)
                {
                    throw new EszamanliDegisiklikException(EszamanliDegisiklikException.KayitMesaji);
                }

                var s = await db.TenantSettings.FirstOrDefaultAsync(ct);
                var isNew = s is null;
                s ??= new Settings();
                apply(s);
                s.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (isNew) db.TenantSettings.Add(s);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new EszamanliDegisiklikException(EszamanliDegisiklikException.KayitMesaji);
        }
    }

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

    public async Task<IReadOnlyList<WhatsAppGonderim>> ListWhatsAppGonderimAsync(int take = 7, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WhatsAppGonderimler.AsNoTracking()
            .OrderByDescending(x => x.Gun).ThenByDescending(x => x.OlusturmaTarihi)
            .Take(take).ToListAsync(ct);
    }
}
