namespace RentACar.Domain.Enums;

/// <summary>BAF (personel araç tahsis) durumu — roadmap L5.</summary>
public enum BafDurum
{
    Acik = 1,       // tahsis edildi / araç personelde
    Kapandi = 2,    // teslim alındı
    Iptal = 3
}
