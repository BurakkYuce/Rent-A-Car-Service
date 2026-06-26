namespace RentACar.Domain.Enums;

/// <summary>Kira sözleşmesi durumu: Kirada → Tamamlandı (dönüş) / İptal.</summary>
public enum RentalStatus
{
    Kirada = 0,
    Tamamlandi = 1,
    Iptal = 2
}
