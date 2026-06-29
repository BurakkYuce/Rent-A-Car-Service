using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.DropTanimlari;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Drop matris kalıcılığı (roadmap N2). (Lokasyon,Sube) benzersiz; 23505→ValidationException.</summary>
public sealed class DropTanimRepository(IDbContextFactory<AppDbContext> factory) : IDropTanimRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<DropTanim>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DropTanimlari.AsNoTracking().OrderBy(c => c.Lokasyon).ThenBy(c => c.Sube).ToListAsync(ct);
    }

    public async Task<DropTanim?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DropTanimlari.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task CreateAsync(DropTanim row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.DropTanimlari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Lokasyon} → {row.Sube}' drop tanımı zaten var."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<DropTanim> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DropTanimlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Lokasyon} → {row.Sube}' drop tanımı zaten var."); }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DropTanimlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        db.DropTanimlari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
