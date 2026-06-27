namespace RentACar.Application.Locations;

/// <summary>Ofis/Lokasyon oluştur/güncelle giriş modeli.</summary>
public sealed class LocationInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Adres { get; set; }
    public string? Telefon { get; set; }
    public string? Sube { get; set; }
    public bool Aktif { get; set; } = true;
}
