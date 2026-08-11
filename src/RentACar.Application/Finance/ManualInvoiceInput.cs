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

    // ---- FAZ-51: bilgi alanları (defter/bakiyeye YANSIMAZ) ----
    public string? IslemSube { get; set; }
    public string? EvrakNo { get; set; }
    public string? FaturaOzelKod { get; set; }
    public string? OdemeTuru { get; set; }
    public string? GonderimSekli { get; set; }
    public string? KdvSifirSebep { get; set; }

    /// <summary>Opsiyonel ÖTV/tevkifat/damga (mevcut `InvoiceTaxInfo`/`ApplyVergi` — kira-faturası
    /// yolunda zaten kullanılan, doğrulanmış tip yeniden kullanılır; yeni alan/doğrulama ÇOĞALTILMAZ).
    /// IadeMi/ManuelMi bu üzerinden GELMEZ — servis bunları kendi invariant'ı olarak zorlar.</summary>
    public InvoiceTaxInfo? Vergi { get; set; }
}
