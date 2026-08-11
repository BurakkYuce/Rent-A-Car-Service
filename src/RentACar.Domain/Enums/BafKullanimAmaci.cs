namespace RentACar.Domain.Enums;

/// <summary>
/// BAF (personel araç tahsis) kullanım amacı — canlı baf_islemleri.aspx "Kullanım Amacı" listesi (11 seçenek).
/// Salt BİLGİ alanıdır: iş kuralı işletmez, defter yazmaz; yalnız tahsisin niçin yapıldığını raporlar.
/// Değerler AÇIKÇA numaralandırılmıştır — DB'ye int olarak yazılır, sıralama değiştirilirse veri kayar.
/// </summary>
public enum BafKullanimAmaci
{
    AracAyirma = 1,
    AracDonusu = 2,
    AracTeslimati = 3,
    Yikama = 4,
    ServisBakim = 5,
    Muayene = 6,
    LastikDegisimi = 7,
    YakitIkmali = 8,
    SubelerArasiTransfer = 9,
    PersonelKullanimi = 10,
    Diger = 11
}
