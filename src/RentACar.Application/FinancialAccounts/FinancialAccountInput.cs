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

    // ---- FAZ-20 ----
    public bool HediyeCek { get; set; }
    public string? OzelKod { get; set; }
    /// <summary>Virgülle ayrılmış e-posta listesi. YALNIZ ALAN — gönderim mantığına bağlı değil.</summary>
    public string? UyariMailListesi { get; set; }
    public bool Aktif { get; set; } = true;
}
