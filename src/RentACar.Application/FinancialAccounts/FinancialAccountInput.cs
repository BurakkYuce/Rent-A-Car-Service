namespace RentACar.Application.FinancialAccounts;

/// <summary>Kasa/Banka hesap oluştur/güncelle giriş modeli.</summary>
public sealed class FinancialAccountInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Tur { get; set; }
    public string? Doviz { get; set; }
    public string? Iban { get; set; }
    public string? HesapNo { get; set; }
    public string? Banka { get; set; }
    public string? Sube { get; set; }
    public bool Aktif { get; set; } = true;
}
