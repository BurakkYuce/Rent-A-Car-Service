namespace RentACar.Application.KdvRates;

/// <summary>KDV oranı tanımı oluştur/güncelle giriş modeli. Oran 0..1 (0.20 = %20).</summary>
public sealed class KdvRateInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public decimal Oran { get; set; }
    public bool Aktif { get; set; } = true;
}
