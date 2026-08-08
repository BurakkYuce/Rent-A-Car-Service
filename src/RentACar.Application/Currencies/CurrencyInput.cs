namespace RentACar.Application.Currencies;

/// <summary>Döviz tanımı oluştur/güncelle giriş modeli. Kod 3 harfli ISO.</summary>
public sealed class CurrencyInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Sembol { get; set; }
    /// <summary>FAZ-20 — ülke (bilgi amaçlı). Kur alanı BİLİNÇLİ olarak yok (tek kaynak: /kurlar).</summary>
    public string? Ulke { get; set; }
    public bool Aktif { get; set; } = true;
}
