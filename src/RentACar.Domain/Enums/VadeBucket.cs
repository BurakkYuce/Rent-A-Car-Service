namespace RentACar.Domain.Enums;

/// <summary>Vade (bitiş) yakınlık kovası — dashboard uyarı paneli.</summary>
public enum VadeBucket
{
    Gecmis = 0,   // bitiş tarihi geçmiş
    YediGun = 1,  // ≤ 7 gün
    OtuzGun = 2,  // ≤ 30 gün
    Ileri = 3     // > 30 gün
}
