using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.TarifeGruplari;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ITarifeGrubuRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + merkezi query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class TarifeGrubuRepository(IDbContextFactory<AppDbContext> factory) : ITariffGroupRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<TarifeGrubu>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TarifeGruplari.AsNoTracking().OrderBy(x => x.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TarifeGrubu>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TarifeGruplari.AsNoTracking().Where(x => x.Aktif).OrderBy(x => x.Ad).ToListAsync(ct);
    }

    public async Task<TarifeGrubu?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TarifeGruplari.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.TarifeGruplari.AsNoTracking()
            .AnyAsync(x => x.Kod == k && (excludeId == null || x.Id != excludeId), ct);
    }

    public async Task CreateAsync(TarifeGrubu row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.TarifeGruplari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu tarife grubu zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<TarifeGrubu> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.TarifeGruplari.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu tarife grubu zaten var.");
        }
        return true;
    }

    /// <summary>
    /// Grubu siler. Bağlı tarife satırları SİLİNMEZ — önce referansları koparılır, sonra grup
    /// silinir; ikisi TEK transaction'da.
    ///
    /// <para>DB tarafında ON DELETE SET NULL kullanılamıyor: FK composite (TenantId, TarifeGrubuId)
    /// ve SET NULL TenantId'yi de null'a çekmeye çalışıp NOT NULL kısıtına çarpıyor. Bu yüzden
    /// kısıt Restrict, "bağ kopar" davranışı burada uygulanıyor.</para>
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.TarifeGruplari.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // RLS + query filter tenant'ı zaten daraltıyor → başka tenant'ın satırına dokunulamaz.
        await db.RateCards.Where(r => r.TarifeGrubuId == id)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.TarifeGrubuId, (Guid?)null), ct);
        db.TarifeGruplari.Remove(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
