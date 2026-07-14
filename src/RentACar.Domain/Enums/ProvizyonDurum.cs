namespace RentACar.Domain.Enums;

/// <summary>Manuel provizyon yaşam döngüsü (FAZ 4.1) — POS'suz kayıt; deftere yazmaz (bilgi/iz).
/// Geçişler: Yok → Alindi → (Kapandi | IadeEdildi); başka geçiş yok.</summary>
public enum ProvizyonDurum
{
    Yok = 0,
    Alindi = 1,
    Kapandi = 2,
    IadeEdildi = 3
}
