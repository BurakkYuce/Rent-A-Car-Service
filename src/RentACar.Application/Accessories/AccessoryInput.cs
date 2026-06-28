namespace RentACar.Application.Accessories;

/// <summary>Aksesuar oluştur/güncelle giriş modeli.</summary>
public sealed class AccessoryInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}
