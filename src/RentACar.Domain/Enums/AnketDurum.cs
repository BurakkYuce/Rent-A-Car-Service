namespace RentACar.Domain.Enums;

/// <summary>
/// Anketin tamamlanma durumu (FAZ-42). "Yapılmadı" da BİR KAYITTIR: müşterinin anketi
/// reddettiği/ulaşılamadığı bilgisi de operasyonel değer taşır ve raporda görünmelidir.
/// </summary>
public enum AnketDurum
{
    Yapilmadi = 0,
    Yapildi = 1
}
