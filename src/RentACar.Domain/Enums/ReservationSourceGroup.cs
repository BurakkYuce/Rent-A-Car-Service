namespace RentACar.Domain.Enums;

/// <summary>
/// Rezervasyon kaynağının üst grubu (FAZ-49). Kaynak matrisinde raporlama/gruplama içindir —
/// hiçbir fiyat/komisyon hesabına girmez.
///
/// <para><b>Sıfır değeri BİLİNÇLİ olarak yok:</b> alan entity'de <c>RezKaynakGrubu?</c> (nullable)
/// tutulur, mevcut satırlar migration sonrası <c>null</c> = "belirtilmemiş" kalır. 0'a bir üye
/// atansaydı geçmiş kayıtların hepsi sessizce o grubu iddia ederdi (migration defaultValue tuzağı).</para>
/// </summary>
public enum ReservationSourceGroup
{
    OfisSatis = 1,
    Broker = 2,
    Acente = 3,
    RentACar = 4,
    Otel = 5,
    Diger = 9
}
