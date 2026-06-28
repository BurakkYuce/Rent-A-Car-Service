namespace RentACar.Application.CustomerGroups;

/// <summary>Müşteri grubu oluştur/güncelle giriş modeli.</summary>
public sealed class CustomerGroupInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
