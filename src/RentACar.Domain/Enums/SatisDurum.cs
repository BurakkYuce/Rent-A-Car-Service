namespace RentACar.Domain.Enums;

/// <summary>Araç satış durumu. Satış kesilince Tamamlandi (defter yazılır, immutable).</summary>
public enum SatisDurum
{
    Tamamlandi = 0,
    Iptal = 1
}
