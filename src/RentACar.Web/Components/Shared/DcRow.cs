namespace RentACar.Web.Components.Shared;

/// <summary>Dashboard Dönüşler/Çıkışlar tablo satırı (Tarih-Saat, Ad Soyad, Plaka, Şube).</summary>
public record DcRow(DateTimeOffset Tar, string Ad, string Plaka, string Sube);
