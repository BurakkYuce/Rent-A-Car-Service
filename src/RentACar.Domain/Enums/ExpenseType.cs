namespace RentACar.Domain.Enums;

/// <summary>Gider türü (araç/genel/personel + regülasyon giderleri).</summary>
public enum ExpenseType
{
    Genel = 0,
    Arac = 1,
    Personel = 2,
    Sigorta = 3,
    Mtv = 4,
    Muayene = 5,
    Diger = 6
}
