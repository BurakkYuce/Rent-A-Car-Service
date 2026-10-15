using Microsoft.EntityFrameworkCore;
using RentACar.Application.EInvoicing;
using RentACar.Domain.Entities;

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
}
