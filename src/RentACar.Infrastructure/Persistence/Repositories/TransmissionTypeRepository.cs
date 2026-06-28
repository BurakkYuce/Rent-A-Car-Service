using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.TransmissionTypes;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ITransmissionTypeRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class TransmissionTypeRepository(IDbContextFactory<AppDbContext> factory) : ITransmissionTypeRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<TransmissionType>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TransmissionTypes.AsNoTracking().OrderBy(t => t.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TransmissionType>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TransmissionTypes.AsNoTracking().Where(t => t.Aktif).OrderBy(t => t.Ad).ToListAsync(ct);
    }

    public async Task<TransmissionType?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TransmissionTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.TransmissionTypes.AsNoTracking()
            .Where(t => t.Kod == k && (excludeId == null || t.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(TransmissionType type, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.TransmissionTypes.Add(type);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{type.Kod}' kodlu vites türü zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<TransmissionType> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var type = await db.TransmissionTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return false;

        apply(type);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{type.Kod}' kodlu vites türü zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var type = await db.TransmissionTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return false;

        db.TransmissionTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
