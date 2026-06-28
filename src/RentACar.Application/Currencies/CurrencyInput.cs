namespace RentACar.Application.Currencies;

/// <summary>Döviz tanımı oluştur/güncelle giriş modeli. Kod 3 harfli ISO.</summary>
public sealed class CurrencyInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Sembol { get; set; }
    public bool Aktif { get; set; } = true;
}
