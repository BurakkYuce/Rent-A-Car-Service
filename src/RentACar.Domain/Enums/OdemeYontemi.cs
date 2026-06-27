namespace RentACar.Domain.Enums;

/// <summary>Gider ödeme yöntemi → defterde karşı hesabı belirler.</summary>
public enum OdemeYontemi
{
    Nakit = 0,      // → Kasa (Alacak)
    Banka = 1,      // → Banka (Alacak)
    AcikHesap = 2   // → tedarikçi Cari (Alacak: ona borçlanırız)
}
