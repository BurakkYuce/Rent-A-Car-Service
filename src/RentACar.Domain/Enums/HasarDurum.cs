namespace RentACar.Domain.Enums;

/// <summary>
/// Hasar dosyası (BAF) onay akışı durumu. Açılır → onaya gönderilir → onaylanır/reddedilir
/// → kapatılır. Mali belge değildir (tahmini tutar bilgilendirme); güncellenebilir.
/// </summary>
public enum HasarDurum
{
    Acik = 0,
    Onayda = 1,
    Onaylandi = 2,
    Reddedildi = 3,
    Kapali = 4
}
