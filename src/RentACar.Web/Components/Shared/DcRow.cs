namespace RentACar.Web.Components.Shared;

/// <summary>Dashboard Dönüşler/Çıkışlar tablo satırı (Tarih-Saat, Ad Soyad, Plaka, Şube).
/// Opsiyonel alanlar satır aksiyonları için: Href satır linki (üretici hedefi belirler — bileşen
/// finans bilmez); RentalId/CariId/Bakiye/Doviz/IslemSayisi hızlı-tahsilat formu içindir ve yalnız
/// Dönüşler satırlarında doldurulur (Çıkışlar=rezervasyon, tahsilat yok → default'lar).</summary>
public record DcRow(DateTimeOffset Tar, string Ad, string Plaka, string Sube,
    string? Href = null, Guid? RentalId = null, Guid? CariId = null,
    decimal Bakiye = 0m, string? Doviz = null, int IslemSayisi = 0);
