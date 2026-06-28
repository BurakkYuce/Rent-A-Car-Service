namespace RentACar.Application.CustomCodes;

/// <summary>Özel kod oluştur/güncelle giriş modeli.</summary>
public sealed class CustomCodeInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}
