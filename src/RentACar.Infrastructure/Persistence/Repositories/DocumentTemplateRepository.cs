using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using Entity = RentACar.Domain.Entities.BelgeSablon;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IBelgeSablonRepository implementasyonu — marka-özel PDF metin şablonu master CRUD.</summary>
public sealed class DocumentTemplateRepository(IDbContextFactory<AppDbContext> factory) : IDocumentTemplateRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Entity>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BelgeSablonlari.AsNoTracking()
            .OrderBy(c => c.BelgeTuru).ThenBy(c => c.Ad).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Entity>> ListByTypeAsync(BelgeTuru type, bool activeOnly, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.BelgeSablonlari.AsNoTracking().Where(c => c.BelgeTuru == type);
        if (activeOnly) q = q.Where(c => c.Aktif);
        return await q.OrderBy(c => c.Ad).ToListAsync(ct);
    }

    public async Task<Entity?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BelgeSablonlari.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Entity?> FindDefaultAsync(BelgeTuru type, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BelgeSablonlari.AsNoTracking()
            .FirstOrDefaultAsync(c => c.BelgeTuru == type && c.VarsayilanMi && c.Aktif, ct);
    }

    public async Task<bool> NameExistsAsync(BelgeTuru type, string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BelgeSablonlari.AsNoTracking()
            .AnyAsync(c => c.BelgeTuru == type && c.Ad == name && (excludeId == null || c.Id != excludeId), ct);
    }

    public async Task CreateAsync(Entity row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.BelgeSablonlari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Ad}' adlı şablon bu belge türünde zaten var."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Entity> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BelgeSablonlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Ad}' adlı şablon bu belge türünde zaten var."); }
        return true;
    }

    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması (<see cref="RowVersionSql"/>).</summary>
    public async Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<Entity> apply, CancellationToken ct = default)
    {
        string? name = null;
        try
        {
            return await RowVersionSql.UpdateAsync(_factory, RowVersionSql.DocumentTemplates, id, expectedVersion,
                (db, k, c) => db.BelgeSablonlari.FirstOrDefaultAsync(x => x.Id == k, c),
                x => { apply(x); name = x.Ad; }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{name}' adlı şablon bu belge türünde zaten var.");
        }
    }

    public async Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await RowVersionSql.ReadAsync(db, RowVersionSql.DocumentTemplates, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BelgeSablonlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        db.BelgeSablonlari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ClearDefaultAsync(BelgeTuru type, Guid exceptId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var others = await db.BelgeSablonlari
            .Where(c => c.BelgeTuru == type && c.VarsayilanMi && c.Id != exceptId).ToListAsync(ct);
        if (others.Count == 0) return;
        foreach (var r in others) r.VarsayilanMi = false;
        await db.SaveChangesAsync(ct);
    }
}
