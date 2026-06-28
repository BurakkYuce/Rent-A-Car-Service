namespace RentACar.Application.FuelKinds;

/// <summary>Yakıt türü oluştur/güncelle giriş modeli.</summary>
public sealed class FuelKindInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
