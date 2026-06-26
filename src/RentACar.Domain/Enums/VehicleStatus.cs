namespace RentACar.Domain.Enums;

/// <summary>
/// Aracın operasyonel durumu. (Orijinal sistemin zengin Arac_Status kümesinin
/// PR #1 için sadeleştirilmiş çekirdeği; ileride genişleyecek.)
/// </summary>
public enum VehicleStatus
{
    Stokta = 0,
    Musait = 1,
    Kirada = 2,
    Serviste = 3,
    Pasif = 4
}
