namespace RentACar.Application.PenaltyTypes;

/// <summary>Ceza türü tanımı oluştur/güncelle giriş modeli. VarsayilanTutar opsiyonel (≥ 0).</summary>
public sealed class PenaltyTypeInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public decimal? VarsayilanTutar { get; set; }
    public bool Aktif { get; set; } = true;
}
