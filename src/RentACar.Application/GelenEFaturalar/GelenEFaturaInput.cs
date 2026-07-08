namespace RentACar.Application.GelenEFaturalar;

/// <summary>Gelen e-Fatura elle giriş modeli (GİB'den çekildiğinde EInvoiceInboxItem'dan da beslenir).</summary>
public sealed class GelenEFaturaInput
{
    public string Ettn { get; set; } = string.Empty;
    public string GonderenVkn { get; set; } = string.Empty;
    public string GonderenUnvan { get; set; } = string.Empty;
    public DateTimeOffset? Tarih { get; set; }
    public decimal NetTutar { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string? Currency { get; set; }
    public string? Aciklama { get; set; }
}
