using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Araç kredisi kalıcılığı (roadmap L4). CreateAsync boşluksuz No (KR-000001) tahsis eder.</summary>
public sealed class AracKrediRepository(IDbContextFactory<AppDbContext> factory) : IAracKrediRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracKredileri.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<AracKredi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracKredileri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AracKredi row, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "AracKrediNo", ct);
            row.No = $"KR-{n:D6}";
            db.AracKredileri.Add(row);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> TaksitOdeAsync(Guid id,
        Func<int, (Expense Expense, IReadOnlyList<AccountLedgerEntry> Entries)>? posting = null,
        CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5 deadlock retry + sayaç yarışı koruması
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Satır kilidi: eşzamanlı taksit ödemeleri serileşir → OdenenTaksit sayaç yarışı
            // (kayıp artırım = kaybolan ödeme) OLMAZ.
            var row = await db.AracKredileri
                .FromSqlRaw("SELECT * FROM \"AracKredileri\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (row is null) return false;
            // Adversarial 1.3 M1: Durum çiti KİLİDİN ARKASINDA — iptal-yarışında iptal krediye para
            // yazılıp İptal'in Kapandi ile ezilmesi imkânsızlaşır (servis ön-kontrolü yarışa açıktı).
            if (row.Durum == KrediDurum.Iptal)
                throw new RentACar.Application.Common.ValidationException("İptal kredinin taksiti ödenemez.");
            if (row.OdenenTaksit >= row.TaksitSayisi) return false; // tüm taksitler ödendi
            row.OdenenTaksit++;
            if (row.OdenenTaksit >= row.TaksitSayisi) row.Durum = KrediDurum.Kapandi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;

            // FAZ 1.3: taksit GİDERİ sayaçla AYNI transaction'da — biri olmadan diğeri asla yazılmaz.
            if (posting is not null)
            {
                var (expense, entries) = posting(row.OdenenTaksit);
                var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
                var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
                if (debit != credit)
                    throw new RentACar.Application.Common.ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");
                var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ExpenseNo", ct);
                expense.No = $"GD-{n:D6}";
                db.Expenses.Add(expense);
                db.AccountLedgerEntries.AddRange(entries);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // IslemAnahtari kısmi unique index: çift-submit → sayaç DA geri alınır (tek tx) → net red.
                await tx.RollbackAsync(ct);
                throw new RentACar.Application.Common.ValidationException("Bu taksit ödemesi zaten kaydedilmiş (çift gönderim).");
            }
            return true;
        }, ct);
    }

    public async Task<bool> SetDurumAsync(Guid id, KrediDurum durum, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Aynı satır kilidi (M1 simetrisi): iptal, süren taksit ödemesiyle serileşir.
            var row = await db.AracKredileri
                .FromSqlRaw("SELECT * FROM \"AracKredileri\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (row is null) return false;
            row.Durum = durum;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }
}
