namespace RentACar.Application.CancelReasons;

/// <summary>İptal sebebi oluştur/güncelle giriş modeli.</summary>
public sealed class CancelReasonInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
