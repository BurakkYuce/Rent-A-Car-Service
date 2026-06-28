namespace RentACar.Application.ReservationSources;

/// <summary>Rezervasyon kaynağı oluştur/güncelle giriş modeli.</summary>
public sealed class ReservationSourceInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
