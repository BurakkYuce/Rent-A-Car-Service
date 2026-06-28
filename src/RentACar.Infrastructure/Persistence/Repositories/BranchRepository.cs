using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IBranchRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + global
/// query filter ile otomatik. Kod benzersizliği DB'de kısmi/normal unique index ile
/// güvencededir; yarış ihlali (23505) ValidationException'a çevrilir.
/// </summary>
public sealed class BranchRepository(IDbContextFactory<AppDbContext> factory) : IBranchRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Branch>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Branches.AsNoTracking().OrderBy(b => b.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Branch>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Branches.AsNoTracking().Where(b => b.Aktif).OrderBy(b => b.Kod).ToListAsync(ct);
    }

    public async Task<Branch?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<Branch?> FindByAdAsync(string ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tenant-scoped (query filter + RLS) + EXACT case-insensitive eşleşme (adversarial M2: ILike pattern
        // "M_rkez"/"%" yanlış eşleşiyordu + backfill exact ile uyumsuzdu). lower()=lower() backfill ile birebir.
        // Kod sırası → aynı adlı şubede deterministik seçim (L2).
        var norm = ad.Trim().ToLowerInvariant();
        return await db.Branches.AsNoTracking()
            .Where(b => b.Ad.ToLower() == norm)
            .OrderBy(b => b.Kod)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Branches.AsNoTracking()
            .Where(b => b.Kod == k && (excludeId == null || b.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Branch branch, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Branches.Add(branch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{branch.Kod}' kodlu şube zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Branch> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch is null) return false;

        apply(branch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{branch.Kod}' kodlu şube zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch is null) return false;

        db.Branches.Remove(branch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // roadmap F1: composite FK Restrict → kullanımdaki şube silinemez (dostça hata).
            throw new ValidationException("Bu şube araç/gider/personel/kural kaydında kullanılıyor; önce bağı kaldırın.");
        }
        return true;
    }
}
