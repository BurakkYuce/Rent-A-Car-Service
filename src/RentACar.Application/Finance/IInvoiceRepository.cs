using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

public interface IInvoiceRepository
{
    Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default);
    Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>FAZ-54 — süzgeçli fatura listesi; cari/araç künyesi çözümlenmiş (PII çözülmez).</summary>
    Task<IReadOnlyList<InvoiceRow>> SearchAsync(InvoiceFilter? filter = null, CancellationToken ct = default);

    /// <summary>
    /// FAZ-52 — fatura SATIRI seviyesinde birleştirilmiş liste (fatura × cari × kira × araç ×
    /// rezervasyon). Salt okuma; para satırdan OLDUĞU GİBİ alınır, yeniden hesaplanmaz.
    /// </summary>
    Task<IReadOnlyList<FaturaSatirDto>> ListLinesAsync(
        FaturaSatirFilter? filter = null, CancellationToken ct = default);

    /// <summary>Bir kiranın TÜM faturaları: base (RentalId) + fark (KaynakKiraId) + bunların iadeleri —
    /// iptal/iade DAHİL (görsel liste; durum rozetiyle ayrışır). Kira formu "Faturalar" alt-sekmesi.</summary>
    Task<IReadOnlyList<Invoice>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Verilen kaynak fatura için zaten bir iade faturası kesilmiş mi? (idempotency ön-kontrol)</summary>
    Task<bool> IadeExistsForAsync(Guid kaynakFaturaId, CancellationToken ct = default);

    /// <summary>Bir kira için fark-hesabı durumu TEK ATOMİK snapshot'ta (RepeatableRead): (a) faturalanmış NET
    /// BRÜT = base+fark (Iptal + iade-faturası hariç) − iade brütü; (b) kesilmiş fark SAYISI (iade dahil = sıra
    /// sayacı). İkisi AYNI snapshot'tan okunur → eşzamanlı fark isteklerinde faturalanan/sıra TUTARLI (TOCTOU
    /// yok): ya ikisi de fark-öncesi (aynı sıra → unique index çakışır) ya ikisi de fark-sonrası (fark=0 red).
    /// Sıradaki fark sıra no = FarkSayisi + 1 (idempotency doğal anahtarı; adversarial Kritik-1 + V6).</summary>
    Task<(decimal FaturalananBrut, int FarkSayisi)> GetFarkStateAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>
    /// Fatura + satırlar + DENGELİ defter kümesini TEK transaction'da işler. No boşluksuz
    /// tahsis edilir. Fatura DB-seviyesinde değişmez (trigger).
    /// </summary>
    Task PostAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>FAZ 4.2-B2: dönem faturası — fatura + satır + defter + DÖNEM SATIRI (Kesildi/InvoiceId/
    /// KesilenTutar) TEK transaction. Advisory kira-fatura kilidi altında FATURALANAN yeniden doğrulanır
    /// (beklenenFaturalanan sapmışsa red — base/fark bu arada commit etmiş olabilir; adversarial
    /// B2-Kritik-1) ve dönem yalnız Planlandi ise kesilir (Kesildi/Atlandi yarış çiti;
    /// KaynakKiraFarkSira unique index ikinci savunma).</summary>
    Task PostDonemAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries,
        Guid donemId, decimal kesilenTutar, decimal beklenenFaturalanan, CancellationToken ct = default);
}
