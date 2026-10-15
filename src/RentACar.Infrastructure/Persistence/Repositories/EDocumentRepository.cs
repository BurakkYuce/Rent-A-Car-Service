using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.EInvoicing;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// e-Belge kayıtları (EBelgeler). Kullanıcı eylemleri satır kilidi (<c>FOR UPDATE</c>) altında ve izlenen varlık
/// üzerinden yazılır → denetim kaydı (AuditLog) düşer. Gönderim işinin yazımları <see cref="EDocumentDispatcher"/>'da.
/// </summary>
public sealed class EDocumentRepository(IDbContextFactory<AppDbContext> factory) : IEDocumentRepository
{
    public async Task<EDocument?> FindByInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.EDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.InvoiceId == invoiceId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, EDocument>> ListByInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct = default)
    {
        if (invoiceIds.Count == 0) return new Dictionary<Guid, EDocument>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = invoiceIds.Distinct().ToList();
        return await db.EDocuments.AsNoTracking().Where(d => ids.Contains(d.InvoiceId)).ToDictionaryAsync(d => d.InvoiceId, ct);
    }

    public Task<EDocumentStatus> QueueAsync(Guid invoiceId, DateTimeOffset now, CancellationToken ct = default)
        => PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM \"EBelgeler\" WHERE \"InvoiceId\" = {0} FOR UPDATE", [invoiceId], ct);
            var doc = await db.EDocuments.FirstOrDefaultAsync(d => d.InvoiceId == invoiceId, ct);
            if (doc is null)
            {
                // Özelliğin öncesinden kalan fatura: kayıt yok → ilk kez kuyruğa alınır (yeni ETTN).
                if (!await db.Invoices.AsNoTracking().AnyAsync(i => i.Id == invoiceId, ct))
                    throw new ValidationException("Fatura bulunamadı.");
                db.EDocuments.Add(new EDocument
                {
                    InvoiceId = invoiceId, Ettn = Guid.NewGuid(), Durum = EDocumentStatus.Gonderilecek, CreatedAtUtc = now,
                });
            }
            else
            {
                switch (doc.Durum)
                {
                    case EDocumentStatus.Gonderilecek:
                        await tx.RollbackAsync(ct);
                        return EDocumentStatus.Gonderilecek; // zaten kuyrukta — sessiz başarı
                    case EDocumentStatus.Gonderildi:
                        throw new ValidationException("e-Belge zaten gönderildi.");
                    case EDocumentStatus.Iptal:
                        throw new ValidationException("e-Belge iptal edilmiş; yeniden gönderilemez.");
                }
                // Taslak/Hata → kuyruk. Deneme sayacı sıfırlanır (yeni deneme hakkı) ama SonDenemeUtc KORUNUR:
                // önceki bir deneme entegratöre ulaşmış olabilir → gönderim işi yeniden göndermeden önce durum sorar.
                doc.Durum = EDocumentStatus.Gonderilecek;
                doc.DenemeSayisi = 0;
                doc.SonrakiDenemeUtc = null;
                doc.KilitBitisUtc = null;
                doc.SonMesaj = null;
                doc.UpdatedAtUtc = now;
            }
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Eşzamanlı ikinci istek kaydı bizden önce açtı → aynı sonuç (kuyrukta).
                await tx.RollbackAsync(ct);
                return EDocumentStatus.Gonderilecek;
            }
            return EDocumentStatus.Gonderilecek;
        }, ct);

    public Task<bool> MarkSentAsync(Guid id, IReadOnlyCollection<EDocumentStatus> expected, string? integratorRef,
        string? integratorStatus, DateTimeOffset now, CancellationToken ct = default)
        => PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM \"EBelgeler\" WHERE \"Id\" = {0} FOR UPDATE", [id], ct);
            var doc = await db.EDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (doc is null || !expected.Contains(doc.Durum)) return false;
            if (doc.KilitBitisUtc is { } lease && lease >= now) return false; // gönderim işi tutuyor
            doc.Durum = EDocumentStatus.Gonderildi;
            doc.GonderimUtc ??= now; // asıl kabul anı bilinmiyor; düzeltmenin yapıldığı an
            doc.EntegratorRef = integratorRef ?? doc.EntegratorRef;
            doc.EntegratorDurum = integratorStatus ?? doc.EntegratorDurum;
            doc.SonMesaj = "Önceki gönderim denemesi entegratöre ulaşmış (durum sorgusuyla doğrulandı).";
            doc.KilitBitisUtc = null;
            doc.SonrakiDenemeUtc = null;
            doc.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);

    public Task<bool> MarkCancelledAsync(Guid id, IReadOnlyCollection<EDocumentStatus> expected, bool requireLeaseFree,
        string? message, DateTimeOffset now, CancellationToken ct = default)
        => PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM \"EBelgeler\" WHERE \"Id\" = {0} FOR UPDATE", [id], ct);
            var doc = await db.EDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (doc is null || !expected.Contains(doc.Durum)) return false;
            if (requireLeaseFree && doc.KilitBitisUtc is { } lease && lease >= now) return false; // gönderim işi tutuyor
            doc.Durum = EDocumentStatus.Iptal;
            doc.SonMesaj = message;
            doc.KilitBitisUtc = null;
            doc.SonrakiDenemeUtc = null;
            doc.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
}
