namespace RentACar.Domain.Enums;

/// <summary>
/// Teklif (quotation) durum makinesi: Taslak→Gonderildi→Kabul/Red. Kabul, teklifi
/// rezervasyona çevirir (Reservation oluşur, teklif Kabul'e geçer + bağlanır).
/// </summary>
public enum QuotationStatus
{
    Taslak = 0,
    Gonderildi = 1,
    Kabul = 2,
    Red = 3
}
