using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task<IReadOnlyList<Invoice>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // base (RentalId) + fark (KaynakKiraId) — GetFarkStateAsync ile aynı kapsam; burada iptal/iade de
        // listelenir (görsel liste, filtre yok). İadeler kaynak fatura üzerinden dolaylı bağlı olduğundan
        // ikinci sorguyla eklenir.
        var kiraFaturalari = await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId == rentalId || i.KaynakKiraId == rentalId)
            .ToListAsync(ct);
        var ids = kiraFaturalari.Select(x => x.Id).ToList();
        var iadeler = ids.Count == 0
            ? []
            : await db.Invoices.AsNoTracking()
                .Where(i => i.KaynakFaturaId != null && ids.Contains(i.KaynakFaturaId.Value))
                .ToListAsync(ct);
        return kiraFaturalari.Concat(iadeler.Where(i => !ids.Contains(i.Id)))
            .OrderByDescending(i => i.Tarih).ThenByDescending(i => i.No).ToList();
    }

    public async Task<bool> IadeExistsForAsync(Guid kaynakFaturaId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == kaynakFaturaId, ct);
    }

    public async Task<(decimal FaturalananBrut, int FarkSayisi)> GetFarkStateAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TOCTOU koruması (adversarial Kritik-1): faturalanan + fark-sayısı AYNI snapshot'tan okunur → eşzamanlı
        // fark isteklerinde tutarlı sıra. RepeatableRead: tek tutarlı görüntü; salt-okuma → rollback.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        try
        {
            return await FarkStateHesaplaAsync(db, rentalId, ct);
        }
        finally { await tx.RollbackAsync(ct); }
    }

    /// <summary>Fark-state (iade-netli faturalanan brüt + fark sayısı) — GetFarkStateAsync ile
    /// posting TX-içi yeniden doğrulaması (adversarial B2-Kritik-1) AYNI sorgudan geçer (tek kopya).</summary>
    private static async Task<(decimal FaturalananBrut, int FarkSayisi)> FarkStateHesaplaAsync(
        AppDbContext db, Guid rentalId, CancellationToken ct)
    {
        // Kira faturaları: base (RentalId) + fark (KaynakKiraId); iptal + iade-faturasının KENDİSİ hariç.
        var kiraFaturalari = await db.Invoices.AsNoTracking()
            .Where(i => (i.RentalId == rentalId || i.KaynakKiraId == rentalId) && i.Durum != InvoiceStatus.Iptal && !i.IadeMi)
            .Select(i => new { i.Id, i.GenelToplam })
            .ToListAsync(ct);
        var gross = kiraFaturalari.Sum(x => x.GenelToplam);
        // İade netleme (adversarial High-2/3): bu kira faturalarına kesilmiş iade brütünü düş (iade
        // GenelToplam pozitif ama defteri TERS döndürür → net faturalanan = base − iade).
        var iadeGross = 0m;
        if (gross != 0m)
        {
            var ids = kiraFaturalari.Select(x => x.Id).ToList();
            iadeGross = await db.Invoices.AsNoTracking()
                .Where(i => i.IadeMi && i.KaynakFaturaId != null && ids.Contains(i.KaynakFaturaId.Value) && i.Durum != InvoiceStatus.Iptal)
                .SumAsync(i => (decimal?)i.GenelToplam, ct) ?? 0m;
        }
        // Fark sayısı (iade edilmiş fark faturanın kendisi de KaynakKiraId'yi korur → sayaçta kalır →
        // yeniden kesim yeni sıra alır, adversarial V6).
        var farkSayisi = await db.Invoices.AsNoTracking()
            .CountAsync(i => i.KaynakKiraId == rentalId && i.Durum != InvoiceStatus.Iptal, ct);
        return (gross - iadeGross, farkSayisi);
    }

    /// <summary>Kira-fatura advisory kilidi (adversarial B2-Kritik-1): base/fark/dönem posting'leri
    /// aynı kira üzerinde SERİLEŞİR — iki farklı unique-index'e yazan yollar (base: (TenantId,RentalId);
    /// fark/dönem: (TenantId,KaynakKiraId,Sıra)) birbirini görmeden commit edemez.</summary>
    private static async Task KiraFaturaKilidiAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"fatura:{db.TenantId}:{rentalId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
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

            // Adversarial B2-Kritik-1: kira-bağlı faturada advisory kilit + TX-İÇİ yeniden doğrulama.
            // BASE yolu yalnız HİÇ fatura yokken çalışır (IsRentalInvoiced kapısı) — kilit altında
            // yeniden bakılır; bu arada dönem/fark faturası commit ettiyse base TAM tutarı ikinci kez
            // keserdi (çift faturalama) → temiz red (operatör fark yoluna düşer).
            var kiraBagi = invoice.RentalId ?? invoice.KaynakKiraId;
            if (kiraBagi is Guid kb && !invoice.IadeMi)
            {
                await KiraFaturaKilidiAsync(db, kb, ct);
                if (invoice.RentalId is Guid rid &&
                    await db.Invoices.AsNoTracking().AnyAsync(i => i.RentalId == rid || i.KaynakKiraId == rid, ct))
                    throw new ValidationException("Kira bu sırada faturalandı (eşzamanlı istek) — kalan tutar için 'Fatura Kes' fark yolunu kullanın.");
            }

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

    public async Task PostDonemAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries,
        Guid donemId, decimal kesilenTutar, decimal beklenenFaturalanan, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Dönem faturası defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Adversarial B2-Kritik-1: advisory kilit + FATURALANAN TX-İÇİ yeniden doğrulama —
            // servis kesileceği kilit DIŞINDA hesapladı; bu arada base/fark/dönem faturası commit
            // ettiyse tutar bayattır → temiz red (çağıran güncel durumla yeniden dener).
            await KiraFaturaKilidiAsync(db, invoice.KaynakKiraId!.Value, ct);
            var (guncelFaturalanan, _) = await FarkStateHesaplaAsync(db, invoice.KaynakKiraId.Value, ct);
            if (guncelFaturalanan != beklenenFaturalanan)
                throw new ValidationException("Kira faturaları bu sırada değişti (eşzamanlı istek) — dönem kesimini yeniden deneyin.");

            // FAZ 4.2-B2: dönem satırı fatura+defterle AYNI transaction'da Kesildi'ye geçer — yarım
            // durum imkânsız (fatura var/dönem Planlandi ya da tersi olamaz). TX-içi yarış çiti:
            // Planlandi DIŞI her durum reddedilir (Kesildi/Atlandi yarışı; unique index ikinci savunma).
            var donem = await db.FaturaDonemleri.FirstOrDefaultAsync(d => d.Id == donemId, ct)
                ?? throw new ValidationException("Fatura dönemi bulunamadı.");
            if (donem.Durum != FaturaDonemDurum.Planlandi)
                throw new ValidationException("Dönem bu sırada kesilmiş/atlanmış (eşzamanlı istek).");
            donem.Durum = FaturaDonemDurum.Kesildi;
            donem.InvoiceId = invoice.Id;
            donem.KesilenTutar = kesilenTutar;
            donem.UpdatedAtUtc = DateTimeOffset.UtcNow;

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "InvoiceNo", ct);
            invoice.No = $"FT-{n:D6}";
            foreach (var entry in entries)
                entry.Description = $"Fatura {invoice.No}";

            db.Invoices.Add(invoice);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw new ValidationException("Dönem faturası zaten kesilmiş (eşzamanlı istek).");
            }
        }, ct);
    }
}
