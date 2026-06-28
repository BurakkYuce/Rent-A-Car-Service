using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IPersonelRepository: kısa-ömürlü context (factory). Tenant izolasyonu RLS + query filter. Kod (sicil)
/// benzersizliği DB unique index; ihlal (23505) ValidationException. PII şifreleme servis katmanında.
/// </summary>
public sealed class PersonelRepository(IDbContextFactory<AppDbContext> factory) : IPersonelRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Personel>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Personeller.AsNoTracking().OrderBy(r => r.Kod).ToListAsync(ct);
    }

    public async Task<Personel?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Personeller.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Personeller.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Personel row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Personeller.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' sicilli personel zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Personel> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Personeller.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' sicilli personel zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Personeller.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.Personeller.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
