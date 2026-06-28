using RentACar.Domain.Enums;

namespace RentACar.Application.CoverageProducts;

/// <summary>Sigorta/ek hizmet ürünü oluştur/güncelle giriş modeli. Fiyat alanları opsiyonel.</summary>
public sealed class CoverageProductInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? AdEn { get; set; }
    public string? Aciklama { get; set; }

    public CoverageProductType Tur { get; set; } = CoverageProductType.Diger;

    public decimal? GunlukUcret { get; set; }
    public decimal? KdvOrani { get; set; }
    public int? MaxGun { get; set; }
    public string? Doviz { get; set; }
    public bool Zorunlu { get; set; }

    public bool Aktif { get; set; } = true;
}
