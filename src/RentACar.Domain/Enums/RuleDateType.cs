namespace RentACar.Domain.Enums;

/// <summary>
/// Kampanya arama ekranındaki (canlı kampanya_ara.aspx) "Tarih Tipi" ayrımı: kuralın geçerlilik
/// aralığının hangi tarihe göre YORUMLANDIĞINI kullanıcıya anlatan sınıflandırma.
///
/// <para><b>BİLGİ ALANI — fiyat motoruna GİRMEZ (KARARLAR.md "yeni alanlar deftere/hesaba
/// yazmaz" genel politikası).</b> <see cref="RentACar.Domain.Entities.RentalRule"/> geçerliliği
/// motorda DAİMA kira BAŞLANGIÇ tarihinden (<c>QuoteRequest.BasTar</c>) kontrol edilir; bu alanın
/// hiçbir değeri o kontrolü değiştirmez. Aksi hâlde aynı kural, yalnız bir sınıflandırma alanı
/// yüzünden farklı fiyat üretirdi — FAZ-73 bir YÜZEY fazıdır, fiyat davranışı değişmez
/// (<c>FiyatMotoruYuzeyTests.TarihTipi_bilgi_alani_fiyat_degismedi</c> bunu kilitler).</para>
///
/// <para>Sayısal değerler KALICIDIR (DB'de int saklanır).</para>
/// </summary>
public enum RuleDateType
{
    /// <summary>Geçerlilik aralığı kiralama/rezervasyon (alış) tarihine göre okunur — VARSAYILAN,
    /// motorun bugünkü tek davranışı.</summary>
    Rezervasyon = 0,

    /// <summary>Geçerlilik aralığı talebin alındığı (satış) tarihine göre okunur — raporlama/arama
    /// sınıflandırması; motor bu ayrımı UYGULAMAZ.</summary>
    Talep = 1
}
