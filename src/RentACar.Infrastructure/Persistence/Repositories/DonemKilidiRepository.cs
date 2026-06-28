using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IDonemKilidiRepository (roadmap D2): tenant başına TEK kilit satırı. Tenant izolasyonu RLS + query
/// filter. Eşzamanlı çift insert unique index (TenantId) ile engellenir (23505 → ValidationException).
/// </summary>
public sealed class DonemKilidiRepository(IDbContextFactory<AppDbContext> factory) : IDonemKilidiRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<DonemKilidi?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DonemKilitleri.AsNoTracking().FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(Action<DonemKilidi> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DonemKilitleri.FirstOrDefaultAsync(ct);
        var isNew = row is null;
        row ??= new DonemKilidi();
        apply(row);
        if (isNew) db.DonemKilitleri.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException("Dönem kilidi zaten kayıtlı (eşzamanlı yazım).");
        }
    }
}
