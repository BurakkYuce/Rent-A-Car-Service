namespace RentACar.Application.Banks;

/// <summary>Banka oluştur/güncelle giriş modeli.</summary>
public sealed class BankInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
