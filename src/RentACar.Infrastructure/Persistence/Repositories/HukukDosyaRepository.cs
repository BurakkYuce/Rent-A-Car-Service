using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Legal;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IHukukDosyaRepository: kısa-ömürlü context (factory). Tenant izolasyonu RLS + query filter. DosyaNo
/// benzersizliği DB unique index; ihlal (23505) ValidationException.
/// </summary>
public sealed class HukukDosyaRepository(IDbContextFactory<AppDbContext> factory) : IHukukDosyaRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HukukDosyalari.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<HukukDosya?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.HukukDosyalari.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> DosyaNoExistsAsync(string dosyaNo, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = dosyaNo.Trim().ToUpperInvariant();
        return await db.HukukDosyalari.AsNoTracking()
            .Where(r => r.DosyaNo == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(HukukDosya row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.HukukDosyalari.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.DosyaNo}' dosya no zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<HukukDosya> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.HukukDosyalari.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.DosyaNo}' dosya no zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.HukukDosyalari.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.HukukDosyalari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
