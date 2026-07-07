using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Fatura kalıcılığı. PostAsync: No tahsisi + fatura + satırlar + DENGELİ defter kümesi
/// → TEK transaction. Fatura/satır/defter immutable (DB trigger).
/// </summary>
public sealed class InvoiceRepository(IDbContextFactory<AppDbContext> factory) : IInvoiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().OrderByDescending(i => i.Tarih).ToListAsync(ct);
    }

    public async Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<bool> IadeExistsForAsync(Guid kaynakFaturaId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == kaynakFaturaId, ct);
    }

    public async Task<decimal> InvoicedGrossForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Kira faturaları: base (RentalId) + fark (KaynakKiraId); iptal + iade-faturasının KENDİSİ hariç.
        var kiraFaturalari = await db.Invoices.AsNoTracking()
            .Where(i => (i.RentalId == rentalId || i.KaynakKiraId == rentalId) && i.Durum != InvoiceStatus.Iptal && !i.IadeMi)
            .Select(i => new { i.Id, i.GenelToplam })
            .ToListAsync(ct);
        var gross = kiraFaturalari.Sum(x => x.GenelToplam);
        if (gross == 0m) return 0m;
        // İade netleme (adversarial High-2/3): bu kira faturalarına kesilmiş iade faturalarının brütünü düş
        // (iade GenelToplam pozitif ama defteri TERS döndürür → net faturalanan = base − iade).
        var ids = kiraFaturalari.Select(x => x.Id).ToList();
        var iadeGross = await db.Invoices.AsNoTracking()
            .Where(i => i.IadeMi && i.KaynakFaturaId != null && ids.Contains(i.KaynakFaturaId.Value) && i.Durum != InvoiceStatus.Iptal)
            .SumAsync(i => (decimal?)i.GenelToplam, ct) ?? 0m;
        return gross - iadeGross;
    }

    public async Task PostAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Fatura defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "InvoiceNo", ct);
            invoice.No = $"FT-{n:D6}";
            // No defter açıklamasında kullanıldığından satırların ait olduğu fatura no'yu yansıt.
            // İade satırları cari ekstrede "İade" etiketiyle görünsün (adversarial Low: eskiden
            // hepsi "Fatura" yazılıyordu).
            var etiket = invoice.IadeMi ? "İade" : "Fatura";
            foreach (var entry in entries)
                entry.Description = $"{etiket} {invoice.No}";

            db.Invoices.Add(invoice);          // satırlar cascade
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Kısmi unique index (eşzamanlı çift fatura/iade) → idempotent reddet. İade yarışında
                // (TenantId,KaynakFaturaId) index'i tetiklenir → doğru ifadeyle reddet.
                await tx.RollbackAsync(ct);
                // Fark faturası (KaynakKiraId): eşzamanlı/çift istek aynı hedefe çarptı → idempotent reddet.
                throw new ValidationException(
                    invoice.KaynakKiraId is not null ? "Kira farkı zaten faturalanmış (eşzamanlı istek)." :
                    invoice.IadeMi ? "Bu fatura zaten iade edilmiş." : "Kira zaten faturalanmış.");
            }
        }, ct);
    }
}
