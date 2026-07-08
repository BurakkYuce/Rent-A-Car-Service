using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.GelenEFaturalar;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IGelenEFaturaRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter ile
/// otomatik. ETTN benzersizliği DB unique index (TenantId,Ettn) ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class GelenEFaturaRepository(IDbContextFactory<AppDbContext> factory) : IGelenEFaturaRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<GelenEFatura>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.GelenEFaturalar.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<GelenEFatura?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.GelenEFaturalar.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> EttnExistsAsync(string ettn, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var e = ettn.Trim();
        return await db.GelenEFaturalar.AsNoTracking().Where(r => r.Ettn == e).AnyAsync(ct);
    }

    public async Task CreateAsync(GelenEFatura row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.GelenEFaturalar.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Ettn}' ETTN'li gelen fatura zaten kayıtlı.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<GelenEFatura> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.GelenEFaturalar.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
