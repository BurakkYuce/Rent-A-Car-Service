using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.DisHizmetler;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IDisHizmetRepository implementasyonu (FAZ 4.3) — posting CashRepository deseni.</summary>
public sealed class OutsourcedServiceRepository(IDbContextFactory<AppDbContext> factory) : IOutsourcedServiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<DisHizmetAlimi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DisHizmetAlimlari.AsNoTracking()
            .Where(d => d.RentalId == rentalId)
            .OrderByDescending(d => d.Tarih).ThenByDescending(d => d.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<DisHizmetAlimi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DisHizmetAlimlari.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task PostAsync(DisHizmetAlimi record, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        IsBalanced(entries);
        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            record.No = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.DisHizmet, ct);
            foreach (var e in entries) e.Description = $"Dış hizmet {record.No} — {record.AlinanHizmet}";

            db.DisHizmetAlimlari.Add(record);
            db.AccountLedgerEntries.AddRange(entries);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // IslemAnahtari kısmi-unique: çift-submit → idempotent red (No sayacı TX ile geri alınır).
                await tx.RollbackAsync(ct);
                throw IdempotencyConstraint.Red(ex, "Bu dış hizmet kaydı zaten girilmiş (çift gönderim).");
            }
        }, ct);
    }

    public async Task CancelAsync(Guid id, IReadOnlyList<AccountLedgerEntry> reverseEntries, CancellationToken ct = default)
    {
        IsBalanced(reverseEntries);
        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // TX-içi durum çiti (eşzamanlı çift iptal → tek ters kayıt).
            var record = await db.DisHizmetAlimlari
                .FromSqlInterpolated($"SELECT * FROM \"DisHizmetAlimlari\" WHERE \"Id\" = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("Dış hizmet kaydı bulunamadı.");
            if (record.Durum == DisHizmetDurum.Iptal)
                throw new ValidationException("Kayıt zaten iptal edilmiş (eşzamanlı istek).");
            record.Durum = DisHizmetDurum.Iptal;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;

            db.AccountLedgerEntries.AddRange(reverseEntries);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    private static void IsBalanced(IReadOnlyList<AccountLedgerEntry> entries)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Dış hizmet defteri dengesiz: borç {debit} ≠ alacak {credit}.");
    }
}
