using RentACar.Application.BelgeSablon;
using RentACar.Application.Common;
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
    string? FirmaUnvan, string? FirmaAdres, string? FirmaTel, string? FirmaMobilTel, string? FirmaMarka,
    string? FirmaVergiDairesi, string? FirmaVergiNo, byte[]? FirmaLogo,
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
    // Tutar dökümü (+ tam teklif bileşenleri: hediye gün / iskonto / hafta sonu — bilgi)
    decimal GunlukUcret, decimal Tutar, decimal FazlaKmBedeli, decimal YakitBedeli, decimal UzatmaBedeli,
    int? HediyeGun, int? FaturalananGun, decimal? IskontoTutar, decimal? HaftaSonuFark,
    decimal EkHizmetToplam, decimal GenelToplam, decimal Tahsilat, decimal Bakiye, string? Doviz,
    decimal? Depozito, decimal? DropUcreti,   // sözleşme sağ sütunu (bilgi; deftere yansımaz)
    IReadOnlyList<SozlesmeEkHizmet> EkHizmetler,
    string? EkKosullar = null, // FAZ 4.4: kira-özel ek koşullar (varsa sözleşme çıktısına basılır)
    // Marka-özel belge şablonu bölümleri (BelgeSablon; null → renderer koddaki varsayılanı basar).
    string? SablonBaslik = null, string? SablonHukukiSol = null, string? SablonHukukiSag = null,
    string? SablonAltBilgi = null,
    // FAZ-80 — şablon fiziksel imza alanını kapatmışsa false; şablon yoksa true
    // (mevcut PDF davranışı birebir korunur).
    bool SablonImzaAlaniGoster = true);

/// <summary>Sözleşme view-model kurucusu (salt-okur; defter/durum değiştirmez).</summary>
public sealed class ContractService(
    IBookingRepository bookings,
    ICustomerRepository customers,
    IVehicleRepository vehicles,
    IPersonnelRepository staff,
    IRentalAddOnRepository addOns,
    TenantSettingsService settings,
    DocumentTemplateResolver documentTemplate)
{
    public async Task<SozlesmeView?> GetAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null) return null;

        var customer = await customers.FindAsync(c.MusteriId, ct);       // Decrypt'li (TC/ehliyet düz)
        var vehicle = await vehicles.FindAsync(c.VehicleId, ct);
        var attachments = await addOns.ListForRentalAsync(rentalId, ct);
        var setting = await settings.GetAsync(ct);
        var template = await documentTemplate.RentalAsync(c.BelgeSablonId, ct); // marka-özel metin bölümleri
        // Ek koşullar: kira-özel metin varsa o, yoksa şablon varsayılanı (kısmi override).
        var additionalTerms = string.IsNullOrWhiteSpace(c.EkKosullar) ? template.EkKosullarVarsayilan : c.EkKosullar;

        string? receiver = null;
        if (c.TeslimAlanPersonelId is Guid pid && await staff.FindAsync(pid, ct) is { } p)
            receiver = $"{p.Ad} {p.Soyad}";

        var second = c.IkinciSurucuId is Guid isid ? await customers.FindAsync(isid, ct) : null; // decrypt'li
        // FAZ-47: kayıtlı 2. sürücü YOKSA misafir (serbest metin) sürücü sözleşmeye basılır. Alan
        // yalnız formda kalıp belgeye geçmeseydi hiç kaydedilmemiş sayılırdı. TC / ehliyet NUMARASI
        // yoktur (şifreli PII; misafir katmanında bilinçli tutulmuyor) → o hücreler boş kalır.
        var guestSecond = second is null
            ? Nz($"{c.IkinciSurucuSerbestAd} {c.IkinciSurucuSerbestSoyad}")
            : null;

        int? used = c.CikisKm is int ck && c.DonusKm is int dk ? Math.Max(0, dk - ck) : null;

        return new SozlesmeView(
            setting.FirmaUnvan, setting.FirmaAdres, setting.FirmaTel, setting.FirmaMobilTel, setting.FirmaMarka,
            // PR-A: basılamayacak logo (absürt küçük / ölçüsü okunamayan) BURADA null'lanır → PDF'in
            // mevcut metin fallback'i (firma markası/ünvanı) devreye girer. Bayt var olduğu sürece
            // fallback tetiklenmiyordu ve 1×1 piksellik dosya sözleşme başlığına leke basıyordu.
            setting.FirmaVergiDairesi, setting.FirmaVergiNo,
            LogoValidationRules.IsPrintable(setting.LogoBytes) ? setting.LogoBytes : null,
            c.SozlesmeNo, c.Durum.ToString(), c.BasTar, c.BitTar, c.Gun,
            c.CikisOfisi, c.DonusOfisi, c.Aciklama,
            customer?.DisplayName ?? "(bilinmeyen cari)", customer?.CepTel, customer?.Email, customer?.Adres,
            customer?.TcKimlik, customer?.EhliyetNo, customer?.EhliyetSinifi, customer?.EhliyetTarihi, customer?.EhliyetYeri,
            customer?.DogumTarihi,
            second?.DisplayName ?? guestSecond, second?.TcKimlik, second?.EhliyetNo,
            second?.EhliyetSinifi ?? c.IkinciSurucuSerbestEhliyetSinifi,
            second?.EhliyetTarihi, second?.EhliyetYeri, second?.DogumTarihi,
            vehicle?.Plaka ?? "—", vehicle?.Marka, vehicle?.Tip, vehicle?.Grup, vehicle?.Yakit?.ToString() ?? "—", vehicle?.ModelYili,
            c.CikisKm, c.DonusKm, used, c.CikisYakit, c.DonusYakit,
            c.KmLimit, c.FazlaKmUcret,
            c.KmHediye, c.BitisSebebi, receiver, c.GercekDonusTar,
            c.GunlukUcret, c.Tutar, c.FazlaKmBedeli, c.YakitBedeli, c.UzatmaBedeli,
            c.HediyeGun, c.FaturalananGun, c.IskontoTutar, c.HaftaSonuFark,
            attachments.Sum(a => a.Toplam), c.GenelToplam, c.Tahsilat, c.Bakiye, c.Doviz,
            c.Depozito, c.DropUcreti,
            attachments.Select(a => new SozlesmeEkHizmet(a.Ad, a.Toplam)).ToList(),
            additionalTerms,
            template.Baslik, template.HukukiMetinSol, template.HukukiMetinSag, template.AltBilgi,
            template.ImzaAlaniGoster);
    }

    /// <summary>Boş/yalnız-boşluk metni null'a indirger (misafir 2. sürücü adı hiç girilmemişse
    /// sözleşmede " " değil, HİÇ 2. sürücü satırı görünsün).</summary>
    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
