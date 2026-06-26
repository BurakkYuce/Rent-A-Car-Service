namespace RentACar.Domain.Enums;

/// <summary>Rezervasyon durum akışı: Rezerv → Onaylı → KirayaCevrildi (Tasfiye) / İptal.</summary>
public enum ReservationStatus
{
    Rezerv = 0,
    Onayli = 1,
    KirayaCevrildi = 2,
    Iptal = 3
}
