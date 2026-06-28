namespace RentACar.Application.PaymentTypes;

/// <summary>Ödeme tipi oluştur/güncelle giriş modeli.</summary>
public sealed class PaymentTypeInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
