namespace RentACar.Application.Branches;

/// <summary>Şube oluşturma/güncelleme giriş modeli.</summary>
public sealed class BranchInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Adres { get; set; }
    public string? Telefon { get; set; }
    public string? Eposta { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Yetkili { get; set; }
    public string? CalismaSaatleri { get; set; }
    public decimal? KomisyonOran { get; set; }
    public string? EvrakNoOnek { get; set; }
    public bool Aktif { get; set; } = true;
}
