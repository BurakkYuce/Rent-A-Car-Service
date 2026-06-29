namespace RentACar.Application.AracSiparisleri;

/// <summary>Araç sipariş oluşturma girişi (roadmap L3).</summary>
public sealed class AracSiparisInput
{
    public string? Tedarikci { get; set; }
    public DateTimeOffset? SiparisTarihi { get; set; }
    public DateTimeOffset? BeklenenTeslim { get; set; }
    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Grup { get; set; }
    public int Adet { get; set; } = 1;
    public decimal BirimFiyat { get; set; }
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public string? Aciklama { get; set; }
}
