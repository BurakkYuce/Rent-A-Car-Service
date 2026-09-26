using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IRateMatrixRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter
/// ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class RateMatrixRepository(IDbContextFactory<AppDbContext> factory) : IRateMatrixRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<RateMatrix>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateMatrices.AsNoTracking().OrderBy(r => r.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RateMatrix>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateMatrices.AsNoTracking().Where(r => r.Aktif).OrderBy(r => r.Ad).ToListAsync(ct);
    }

    public async Task<RateMatrix?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RateMatrices.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = code.Trim().ToUpperInvariant();
        return await db.RateMatrices.AsNoTracking()
            .Where(r => r.Kod == k && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(RateMatrix row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RateMatrices.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu tarife matrisi zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<RateMatrix> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RateMatrices.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Kod}' kodlu tarife matrisi zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RateMatrices.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        db.RateMatrices.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ADVERSARIAL H1 — KİLİTLİ YENİDEN OKUMA ŞART.
        // Kilitsiz bir "SELECT … WHERE OnayDurumu=Bekliyor" TOCTOU penceresini KAPATMIYORDU:
        // EF'in ürettiği DELETE yalnız "Id = @p" yüklemini taşır, READ COMMITTED'da rakip
        // oturum satırı onaylayıp COMMIT edince PostgreSQL yalnız Id'yi yeniden değerlendirir
        // ve ARTIK ONAYLI olan satır silinirdi (probe ile ampirik kanıtlandı).
        // FOR UPDATE ile: kilit serbest kaldığında PG yüklemi satırın YENİ sürümüne göre
        // yeniden değerlendirir → onaylanan satır adaylıktan DÜŞER.
        //
        // ExecuteDeleteAsync ile çözülmedi: o yol SaveChanges'i atlar, AuditSaveChangesInterceptor
        // çalışmaz ve toplu silme İZSİZ kalırdı.
        var idList = ids as Guid[] ?? [.. ids];
        var waiting = (int)TariffApprovalStatus.Bekliyor;
        var locked = await db.Database.SqlQuery<Guid>(
            $"""SELECT "Id" AS "Value" FROM "TarifeMatris" WHERE "Id" = ANY({idList}) AND "OnayDurumu" = {waiting} FOR UPDATE""")
            .ToListAsync(ct);
        if (locked.Count == 0) { await tx.CommitAsync(ct); return 0; }

        var rows = await db.RateMatrices.Where(r => locked.Contains(r.Id)).ToListAsync(ct);
        db.RateMatrices.RemoveRange(rows);

        try
        {
            await db.SaveChangesAsync(ct);   // tek transaction — yarım parti kalmaz
        }
        catch (DbUpdateConcurrencyException)
        {
            // Satırlar araya giren bir oturumca silinmiş: 500 yerine anlaşılır red (ADVERSARIAL L1).
            await tx.RollbackAsync(ct);
            throw new ValidationException("Tarife satırları başka bir oturumda değişti; listeyi yenileyip tekrar deneyin.");
        }

        await tx.CommitAsync(ct);
        return rows.Count;
    }
}
