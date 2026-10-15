using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.EInvoicing;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// Faturanın e-belge (e-Fatura/e-Arşiv) durumu ve kullanıcı eylemleri:
/// <list type="bullet">
/// <item><c>POST /faturalar/{id}/e-belge/gonder</c> — FinanceWrite: Taslak/Hata (ya da kaydı olmayan eski fatura) →
/// gönderim kuyruğu. Entegratör bağlı değilse 400 ("e-Fatura gönderilmedi: entegratör bağlı değil (…)"). Zaten
/// kuyruktaysa sessiz başarı. Gönderimi istek BEKLEMEZ — gönderim işi alır.</item>
/// <item><c>POST /faturalar/{id}/e-belge/iptal</c> — FinanceReverse, gerekçe zorunlu: gönderilmemiş belge yerel olarak,
/// gönderilmiş e-Arşiv entegratörde iptal edilir; gönderilmiş e-Fatura reddedilir.</item>
/// </list>
/// Kapsam: faturanın şubesi (detayla aynı kural) — kapsam kontrolü durum işinden ÖNCE.
/// </summary>
public static partial class InvoiceUiApi
{
    private static async Task<Results<Ok<EDocumentDto>, ProblemHttpResult>> QueueEDocument(
        Guid id, InvoiceService invoices, EDocumentService edocs, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        var inv = await invoices.GetAsync(id, ct);
        if (inv is null) return F5Shared.NotFound("Fatura bulunamadı.");
        await using (var db = await dbf.CreateDbContextAsync(ct))
            RequireInScope(user, (await InvoiceBranchesAsync(db, [inv], ct))[inv.Id]);
        await edocs.QueueAsync(id, ct);
        return TypedResults.Ok(await EDocumentOfAsync(inv, edocs, ct));
    }

    private static async Task<Results<Ok<EDocumentDto>, ProblemHttpResult>> CancelEDocument(
        Guid id, EDocumentCancelRequest req, InvoiceService invoices, EDocumentService edocs, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var inv = await invoices.GetAsync(id, ct);
        if (inv is null) return F5Shared.NotFound("Fatura bulunamadı.");
        await using (var db = await dbf.CreateDbContextAsync(ct))
            RequireInScope(user, (await InvoiceBranchesAsync(db, [inv], ct))[inv.Id]);
        Text(req.Gerekce, EDocumentService.ReasonMaxLength, "gerekce");
        await edocs.CancelAsync(id, req.Gerekce, ct);
        return TypedResults.Ok(await EDocumentOfAsync(inv, edocs, ct));
    }

    /// <summary>Detay ve eylem yanıtlarının ortak e-belge görünümü (ETTN yalnız gönderilmişse).</summary>
    internal static async Task<EDocumentDto> EDocumentOfAsync(Invoice inv, EDocumentService edocs, CancellationToken ct)
    {
        var doc = await edocs.GetByInvoiceAsync(inv.Id, ct);
        var connected = await edocs.ConnectionGapAsync(ct) is null;
        var invoiceSendable = !inv.IadeMi && inv.Durum != InvoiceStatus.Iptal;
        if (doc is null)
            return new EDocumentDto(null, null, null, null, null, null, 0, null,
                inv.IadeMi ? EDocumentPolicy.RefundNotSent : "Bu fatura e-belge kaydı açılmadan önce kesildi.",
                connected, connected && invoiceSendable, false);

        return new EDocumentDto(
            doc.Durum.ToString(), doc.Tur?.ToString(), doc.Profil, doc.FaturaTipi,
            doc.GonderimUtc is null ? null : doc.Ettn.ToString("D").ToUpperInvariant(),
            doc.GonderimUtc, doc.DenemeSayisi, doc.Durum == EDocumentStatus.Gonderilecek ? doc.SonrakiDenemeUtc : null,
            doc.SonMesaj, connected,
            Gonderilebilir: connected && invoiceSendable && doc.Durum is EDocumentStatus.Taslak or EDocumentStatus.Hata,
            IptalEdilebilir: doc.Durum is EDocumentStatus.Taslak or EDocumentStatus.Hata or EDocumentStatus.Gonderilecek
                             || (doc.Durum == EDocumentStatus.Gonderildi && doc.Tur == EDocumentKind.EArsiv));
    }
}
