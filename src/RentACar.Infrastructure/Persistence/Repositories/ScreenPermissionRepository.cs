using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IScreenPermissionRepository (roadmap E3): ekran override deposu. Tenant izolasyonu RLS + query filter.
/// EkranKodu benzersiz (TenantId, EkranKodu); upsert kod'a göre. 23505 → ValidationException.
/// </summary>
public sealed class ScreenPermissionRepository(IDbContextFactory<AppDbContext> factory) : IScreenPermissionRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<ScreenPermission>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.EkranYetkileri.AsNoTracking().OrderBy(r => r.EkranKodu).ToListAsync(ct);
    }

    public async Task<ScreenPermission?> FindByKodAsync(string ekranKodu, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.EkranYetkileri.AsNoTracking().FirstOrDefaultAsync(r => r.EkranKodu == ekranKodu, ct);
    }

    public async Task UpsertAsync(string ekranKodu, Action<ScreenPermission> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.EkranYetkileri.FirstOrDefaultAsync(r => r.EkranKodu == ekranKodu, ct);
        var isNew = row is null;
        row ??= new ScreenPermission { EkranKodu = ekranKodu };
        apply(row);
        if (isNew) db.EkranYetkileri.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{ekranKodu}' ekran yetkisi zaten kayıtlı (eşzamanlı yazım).");
        }
    }

    public async Task<bool> DeleteAsync(string ekranKodu, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.EkranYetkileri.FirstOrDefaultAsync(r => r.EkranKodu == ekranKodu, ct);
        if (row is null) return false;
        db.EkranYetkileri.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
