using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.EInvoicing;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Yeni kesilen faturanın e-belge kaydını, faturayı yazan context'e (AYNI transaction) ekler. Faturayı yazan üç yolun
/// (kira/manuel/iade/fark: <c>InvoiceRepository.PostAsync</c>, dönem: <c>PostPeriodAsync</c>, dönem job'ı:
/// <c>PeriodInvoiceGenerator</c>) TEK ortak noktası — biri unutulursa o yolun faturaları e-belgesiz kalırdı.
/// Kuyruğa alma kararı saf kuraldadır (<see cref="EDocumentPolicy.ForNewInvoice"/>): ayarlar hazır değilse Taslak +
/// "bağlı değil" sebebi.
/// </summary>
public static class EDocumentQueue
{
    public static async Task AddForNewInvoiceAsync(AppDbContext db, Invoice invoice, DateTimeOffset now, CancellationToken ct)
    {
        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        db.EDocuments.Add(EDocumentPolicy.ForNewInvoice(invoice, settings, now));
    }

    /// <summary>
    /// İade kesilirken (iadeyi yazan transaction İÇİNDE) kaynak faturanın e-belgesini sonuçlandırır (adversarial M1):
    /// defterde geri alınmış bir satış GİB'e gönderilmesin.
    /// <list type="bullet">
    /// <item>Kayıt yok (özellik öncesi fatura) → İptal kaydı eklenir.</item>
    /// <item>Gönderilmemiş ve hiç denenmemiş (Taslak/Gönderilecek/Hata) ya da yalnız TEST ortamına gitmiş → yerel iptal.</item>
    /// <item>Gönderim işi satırı şu an tutuyorsa (kilit) → iade REDDEDİLİR: belge o an entegratöre gidiyor olabilir.</item>
    /// <item>Daha önce denenmiş ama sonucu belirsiz (kayıp yanıt) → dokunulmaz: belge entegratöre ulaşmış olabilir.
    /// Gönderim işi önce entegratöre sorar (ulaşmışsa "gönderildi" yazar, ulaşmamışsa iade kontrolüyle iptal eder);
    /// kuyruğa alma iadesi olan faturayı reddeder.</item>
    /// <item>Gönderilmiş / iptal → dokunulmaz (gönderilmiş e-Arşiv iadeden sonra kullanıcı eylemiyle iptal edilir).</item>
    /// </list>
    /// Satır kilidi (<c>FOR UPDATE</c>) gönderim işinin kilit alma UPDATE'iyle serileşir: iade önce kilitlerse iş satırı
    /// iptal edilmiş bulur; iş önce aldıysa iade kilidi görüp reddeder.
    /// </summary>
    public static async Task SettleSourceOnRefundAsync(AppDbContext db, Guid sourceInvoiceId, DateTimeOffset now, CancellationToken ct)
    {
        await LockAsync(db, sourceInvoiceId, ct);
        var doc = await db.EDocuments.FirstOrDefaultAsync(d => d.InvoiceId == sourceInvoiceId, ct);
        if (doc is null)
        {
            // Özellik öncesi fatura (kayıt yok): İptal kaydı açılır (adversarial N2). Kuyruğa alma aynı fatura kilidini
            // bekler ve iade commit edince iadeyi görüp reddeder; kayıt yoksa sonradan "Gönderime al" yine yeni kayıt açardı.
            db.EDocuments.Add(new EDocument
            {
                TenantId = db.TenantId, InvoiceId = sourceInvoiceId, Ettn = Guid.NewGuid(), Durum = EDocumentStatus.Iptal,
                SonMesaj = EDocumentPolicy.RefundedSourceCancelled, CreatedAtUtc = now,
            });
            return;
        }
        if (doc.Durum is EDocumentStatus.Gonderildi or EDocumentStatus.Iptal) return;
        if (doc.KilitBitisUtc is { } lease && lease >= now)
            throw new ValidationException("Kaynak faturanın e-belgesi şu anda gönderiliyor; birkaç dakika sonra yeniden deneyin.");
        if (doc.SonDenemeUtc is not null && doc.Durum != EDocumentStatus.TestGonderildi) return;

        doc.Durum = EDocumentStatus.Iptal;
        doc.SonMesaj = EDocumentPolicy.RefundedSourceCancelled;
        doc.KilitBitisUtc = null;
        doc.SonrakiDenemeUtc = null;
        doc.UpdatedAtUtc = now;
    }

    /// <summary>
    /// Faturanın e-belge kilidi (aktif transaction'da): <c>ebelge:{tenant}:{fatura}</c> advisory kilidi + varsa satırın
    /// <c>FOR UPDATE</c>'i. Satır YOKKEN de serileştirir — iade ile kuyruğa alma kayıtsız faturada birbirini beklesin
    /// (adversarial N2; satır kilidi var olmayan satırı kilitleyemez). Sıra: önce advisory, sonra satır.
    /// </summary>
    internal static async Task LockAsync(AppDbContext db, Guid invoiceId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}, 42))", [$"ebelge:{db.TenantId}:{invoiceId}"], ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"EBelgeler\" WHERE \"InvoiceId\" = {0} FOR UPDATE", [invoiceId], ct);
    }
}
