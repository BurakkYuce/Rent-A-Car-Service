namespace RentACar.Application.InsuranceCompanies;

/// <summary>Sigorta şirketi oluştur/güncelle giriş modeli.</summary>
public sealed class InsuranceCompanyInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public bool Aktif { get; set; } = true;
}
