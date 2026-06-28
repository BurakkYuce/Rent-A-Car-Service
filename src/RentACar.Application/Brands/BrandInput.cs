namespace RentACar.Application.Brands;

/// <summary>Marka tanımı oluştur/güncelle giriş modeli.</summary>
public sealed class BrandInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
