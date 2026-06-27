namespace RentACar.Application.Pricing;

/// <summary>Tarife (rate card) oluştur/güncelle giriş modeli.</summary>
public sealed class RateCardInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string Grup { get; set; } = string.Empty;
    public int MinGun { get; set; } = 1;
    public int MaxGun { get; set; } = 9999;
    public decimal GunlukUcret { get; set; }
    public string Doviz { get; set; } = "TRY";
    public DateTimeOffset? GecerliBas { get; set; }
    public DateTimeOffset? GecerliBit { get; set; }
    public bool Aktif { get; set; } = true;
}
