namespace RentACar.Application.TransmissionTypes;

/// <summary>Vites türü oluştur/güncelle giriş modeli.</summary>
public sealed class TransmissionTypeInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
