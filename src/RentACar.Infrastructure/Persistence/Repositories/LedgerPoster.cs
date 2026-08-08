using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Belgesiz, DENGELİ defter yazıcı (No tahsisi yok). Yansıtmalar (HGS vb.) gibi doğrudan
/// defter kayıtları için. Σ Borç(base) = Σ Alacak(base) zorunlu; aksi halde ValidationException.
/// TenantId damgası audit interceptor'ı tarafından (ITenantOwned) atılır. TEK transaction.
/// </summary>
public sealed class LedgerPoster(IDbContextFactory<AppDbContext> factory) : ILedgerPoster
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public Task PostAsync(IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
        => YazAsync(entries, null, ct);

    public Task PostWithAsync<T>(IReadOnlyList<AccountLedgerEntry> entries, T ekKayit,
        CancellationToken ct = default) where T : class
        => YazAsync(entries, ekKayit, ct);

    private async Task YazAsync(IReadOnlyList<AccountLedgerEntry> entries, object? ekKayit, CancellationToken ct)
    {
        if (entries.Count == 0) return;

        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.AccountLedgerEntries.AddRange(entries);
            // Künye AYNI transaction'da: biri yazılıp diğeri yazılmadan kalamaz.
            if (ekKayit is not null) db.Add(ekKayit);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // İDEMPOTENT: Bu kayıt kümesi (deterministik SourceId) zaten yazılmış (kısmi
                // unique index). Çift borçlanmayı DB engelledi → sessiz no-op (retry güvenli).
                await tx.RollbackAsync(ct);
            }
        }, ct);
    }
}
