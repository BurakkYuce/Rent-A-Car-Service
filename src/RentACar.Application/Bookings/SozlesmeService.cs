using RentACar.Application.Customers;
using RentACar.Application.Personnel;
using RentACar.Application.RentalAddOns;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;

namespace RentACar.Application.Bookings;

/// <summary>Sözleşme çıktısı ek-hizmet satırı.</summary>
public sealed record SozlesmeEkHizmet(string Ad, decimal Toplam);

/// <summary>
/// Kira sözleşmesi çıktısının TEK veri projeksiyonu — HTML-print (RentalPrint.razor) VE QuestPDF
/// (PdfExportService.Contract) AYNI modeli tüketir; içerik tek kaynaktan, yalnız sunum ayrışır
/// (adversarial inceleme 5c: iki-çıktı senkron borcu). PII (TC/ehliyet) CustomerRepository.Decrypt'ten
/// çözülmüş gelir; tüketici sayfalar mevcut yetki kapısının ([Authorize] + tenant RLS) arkasındadır.
/// </summary>
public sealed record SozlesmeView(
    // Firma başlığı (TenantSettings)
    string? FirmaUnvan, string? FirmaAdres, string? FirmaTel, string? FirmaVergiDairesi, string? FirmaVergiNo,
    // Sözleşme
    string SozlesmeNo, string Durum, DateTimeOffset BasTar, DateTimeOffset BitTar, int Gun,
    string? CikisOfisi, string? DonusOfisi, string? Aciklama,
    // Müşteri / sürücü (decrypt'li)
    string MusteriAd, string? MusteriTel, string? MusteriEmail, string? MusteriAdres,
    string? TcKimlik, string? EhliyetNo, string? EhliyetSinifi, DateTimeOffset? EhliyetTarihi, string? EhliyetYeri,
    DateTimeOffset? DogumTarihi,
    // 2. sürücü (opsiyonel; decrypt'li) — null → tek sürücü
    string? IkinciSurucuAd, string? IkinciTcKimlik, string? IkinciEhliyetNo, string? IkinciEhliyetSinifi,
    DateTimeOffset? IkinciEhliyetTarihi, string? IkinciEhliyetYeri, DateTimeOffset? IkinciDogumTarihi,
    // Araç
    string Plaka, string? Marka, string? Tip, string? Grup, string Yakit, int? ModelYili,
    // KM / yakıt / dönüş
    int? CikisKm, int? DonusKm, int? KullanilanKm, int? CikisYakit, int? DonusYakit,
    int KmLimit, decimal FazlaKmUcret,
    int? KmHediye, string? BitisSebebi, string? TeslimAlanAd, DateTimeOffset? GercekDonusTar,
    // Tutar dökümü
    decimal GunlukUcret, decimal Tutar, decimal FazlaKmBedeli, decimal YakitBedeli, decimal UzatmaBedeli,
    decimal EkHizmetToplam, decimal GenelToplam, decimal Tahsilat, decimal Bakiye, string? Doviz,
    IReadOnlyList<SozlesmeEkHizmet> EkHizmetler);

/// <summary>Sözleşme view-model kurucusu (salt-okur; defter/durum değiştirmez).</summary>
public sealed class SozlesmeService(
    IBookingRepository bookings,
    ICustomerRepository customers,
    IVehicleRepository vehicles,
    IPersonelRepository personeller,
    IRentalAddOnRepository addOns,
    TenantSettingsService settings)
{
    public async Task<SozlesmeView?> GetAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null) return null;

        var musteri = await customers.FindAsync(c.MusteriId, ct);       // Decrypt'li (TC/ehliyet düz)
        var arac = await vehicles.FindAsync(c.VehicleId, ct);
        var ekler = await addOns.ListForRentalAsync(rentalId, ct);
        var ayar = await settings.GetAsync(ct);

        string? teslimAlan = null;
        if (c.TeslimAlanPersonelId is Guid pid && await personeller.FindAsync(pid, ct) is { } p)
            teslimAlan = $"{p.Ad} {p.Soyad}";

        var ikinci = c.IkinciSurucuId is Guid isid ? await customers.FindAsync(isid, ct) : null; // decrypt'li

        int? kullanilan = c.CikisKm is int ck && c.DonusKm is int dk ? Math.Max(0, dk - ck) : null;

        return new SozlesmeView(
            ayar.FirmaUnvan, ayar.FirmaAdres, ayar.FirmaTel, ayar.FirmaVergiDairesi, ayar.FirmaVergiNo,
            c.SozlesmeNo, c.Durum.ToString(), c.BasTar, c.BitTar, c.Gun,
            c.CikisOfisi, c.DonusOfisi, c.Aciklama,
            musteri?.DisplayName ?? "(bilinmeyen cari)", musteri?.CepTel, musteri?.Email, musteri?.Adres,
            musteri?.TcKimlik, musteri?.EhliyetNo, musteri?.EhliyetSinifi, musteri?.EhliyetTarihi, musteri?.EhliyetYeri,
            musteri?.DogumTarihi,
            ikinci?.DisplayName, ikinci?.TcKimlik, ikinci?.EhliyetNo, ikinci?.EhliyetSinifi,
            ikinci?.EhliyetTarihi, ikinci?.EhliyetYeri, ikinci?.DogumTarihi,
            arac?.Plaka ?? "—", arac?.Marka, arac?.Tip, arac?.Grup, arac?.Yakit.ToString() ?? "—", arac?.ModelYili,
            c.CikisKm, c.DonusKm, kullanilan, c.CikisYakit, c.DonusYakit,
            c.KmLimit, c.FazlaKmUcret,
            c.KmHediye, c.BitisSebebi, teslimAlan, c.GercekDonusTar,
            c.GunlukUcret, c.Tutar, c.FazlaKmBedeli, c.YakitBedeli, c.UzatmaBedeli,
            ekler.Sum(a => a.Toplam), c.GenelToplam, c.Tahsilat, c.Bakiye, c.Doviz,
            ekler.Select(a => new SozlesmeEkHizmet(a.Ad, a.Toplam)).ToList());
    }
}
