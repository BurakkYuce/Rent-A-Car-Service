namespace RentACar.Application.Countries;

/// <summary>Ülke oluştur/güncelle giriş modeli.</summary>
public sealed class CountryInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
