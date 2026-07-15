using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.DisHizmetler;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IDisHizmetRepository implementasyonu (FAZ 4.3) — posting CashRepository deseni.</summary>
public sealed class DisHizmetRepository(IDbContextFactory<AppDbContext> factory) : IDisHizmetRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<DisHizmetAlimi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DisHizmetAlimlari.AsNoTracking()
            .Where(d => d.RentalId == rentalId)
            .OrderByDescending(d => d.Tarih).ThenByDescending(d => d.No).ToListAsync(ct);
    }

    public async Task<DisHizmetAlimi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DisHizmetAlimlari.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task PostAsync(DisHizmetAlimi kayit, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        Dengeli(entries);
        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "DisHizmetNo", ct);
            kayit.No = $"DH-{n:D6}";
            foreach (var e in entries) e.Description = $"Dış hizmet {kayit.No} — {kayit.AlinanHizmet}";

            db.DisHizmetAlimlari.Add(kayit);
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
                throw new ValidationException("Bu dış hizmet kaydı zaten girilmiş (çift gönderim).");
            }
        }, ct);
    }

    public async Task IptalAsync(Guid id, IReadOnlyList<AccountLedgerEntry> tersEntries, CancellationToken ct = default)
    {
        Dengeli(tersEntries);
        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // TX-içi durum çiti (eşzamanlı çift iptal → tek ters kayıt).
            var kayit = await db.DisHizmetAlimlari
                .FromSqlInterpolated($"SELECT * FROM \"DisHizmetAlimlari\" WHERE \"Id\" = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("Dış hizmet kaydı bulunamadı.");
            if (kayit.Durum == DisHizmetDurum.Iptal)
                throw new ValidationException("Kayıt zaten iptal edilmiş (eşzamanlı istek).");
            kayit.Durum = DisHizmetDurum.Iptal;
            kayit.UpdatedAtUtc = DateTimeOffset.UtcNow;

            db.AccountLedgerEntries.AddRange(tersEntries);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    private static void Dengeli(IReadOnlyList<AccountLedgerEntry> entries)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Dış hizmet defteri dengesiz: borç {debit} ≠ alacak {credit}.");
    }
}
