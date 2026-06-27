using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Penalties;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Ceza kalıcılığı. Yansıtma satır kilidiyle (FOR UPDATE) idempotenttir.</summary>
public sealed class PenaltyRepository(IDbContextFactory<AppDbContext> factory) : IPenaltyRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Penalty>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Penalties.AsNoTracking().OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Penalty?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Penalties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task CreateAsync(Penalty penalty, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "PenaltyNo", ct);
        penalty.No = $"CZ-{n:D6}";
        db.Penalties.Add(penalty);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Penalty> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var p = await db.Penalties.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return false;
        apply(p);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReflectAsync(
        Guid id, Func<Penalty, IReadOnlyList<AccountLedgerEntry>> buildEntries, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Satır kilidi: eşzamanlı yansıtmalar serileşir → çift yansıtma olmaz (idempotent).
        var penalty = await db.Penalties
            .FromSqlRaw("SELECT * FROM \"Penalties\" WHERE \"Id\" = {0} FOR UPDATE", id)
            .FirstOrDefaultAsync(ct);
        if (penalty is null) return false;
        if (penalty.Durum != CezaDurum.Yeni) return false; // zaten yansıtılmış/işlenmiş

        var entries = buildEntries(penalty);
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Ceza yansıtma defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        penalty.Durum = CezaDurum.Yansitildi;
        penalty.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.AccountLedgerEntries.AddRange(entries);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
