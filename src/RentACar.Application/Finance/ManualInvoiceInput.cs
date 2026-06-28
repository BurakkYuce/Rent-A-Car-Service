namespace RentACar.Application.Finance;

/// <summary>Manuel/serbest fatura + iade faturası girişi (roadmap G2). KdvOrani kesir (0.20 = %20).</summary>
public sealed class ManualInvoiceInput
{
    public Guid CariId { get; set; }
    public string? Aciklama { get; set; }
    public decimal NetTutar { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m;
    public DateTimeOffset? Tarih { get; set; }
    public DateTimeOffset? VadeTarihi { get; set; }
    /// <summary>Opsiyonel idempotency anahtarı — verilirse çift-submit aynı faturayı döndürür.</summary>
    public Guid? IslemAnahtari { get; set; }
}
