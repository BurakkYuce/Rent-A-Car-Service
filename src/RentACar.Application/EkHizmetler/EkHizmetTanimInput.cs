namespace RentACar.Application.EkHizmetler;

/// <summary>Ek hizmet tanımı oluştur/güncelle giriş modeli.</summary>
public sealed class EkHizmetTanimInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public decimal BirimUcret { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m;
    public bool Aktif { get; set; } = true;
}
