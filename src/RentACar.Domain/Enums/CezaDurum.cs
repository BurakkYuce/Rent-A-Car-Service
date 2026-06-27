namespace RentACar.Domain.Enums;

/// <summary>Ceza durumu.</summary>
public enum CezaDurum
{
    Yeni = 0,
    Yansitildi = 1,  // müşteriye yansıtıldı (cari borçlandı)
    Odendi = 2,
    Iptal = 3
}
