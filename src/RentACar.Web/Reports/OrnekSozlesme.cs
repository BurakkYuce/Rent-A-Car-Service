using RentACar.Application.Bookings;

namespace RentACar.Web.Reports;

/// <summary>
/// Örnek (şablon) kira sözleşmesi view-model'i — gerçek kira kaydı olmadan sözleşme PDF'inin nasıl
/// göründüğünü gösteren RentPro markalı numune. TÜM veriler açık ÖRNEK (gerçek müşteri/PII YOK).
/// <c>/kiralar/ornek-sozlesme/pdf</c> ucu bunu <see cref="PdfExportService.Contract"/> ile render eder;
/// sözleşme çıktısı (HTML-print + PDF) ile TEK veri modeli (SozlesmeView) — ayrı şablon tutulmaz.
/// </summary>
public static class OrnekSozlesme
{
    public static SozlesmeView Ornek() => new(
        // Firma başlığı (RentPro markası)
        FirmaUnvan: "RentPro Araç Kiralama A.Ş.", FirmaAdres: "Örnek Mah. Kiralama Cad. No:1, İstanbul",
        FirmaTel: "0212 000 00 00", FirmaMobilTel: "0555 000 00 00", FirmaMarka: "RentPro",
        FirmaVergiDairesi: "Örnek V.D.", FirmaVergiNo: "1234567890",
        // Sözleşme
        SozlesmeNo: "ORNEK-0001", Durum: "Örnek",
        BasTar: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
        BitTar: new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero), Gun: 3,
        CikisOfisi: "MERKEZ", DonusOfisi: "MERKEZ",
        Aciklama: "Bu bir ÖRNEK sözleşmedir; gerçek müşteri verisi içermez.",
        // Müşteri / sürücü (örnek — PII değil)
        MusteriAd: "Örnek Müşteri", MusteriTel: "0555 111 22 33", MusteriEmail: "ornek@rentpro.example",
        MusteriAdres: "Örnek Adres, İstanbul",
        TcKimlik: "11111111110", EhliyetNo: "ORNEK123", EhliyetSinifi: "B",
        EhliyetTarihi: new DateTimeOffset(2015, 6, 1, 0, 0, 0, TimeSpan.Zero), EhliyetYeri: "İSTANBUL",
        DogumTarihi: new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero),
        // 2. sürücü yok
        IkinciSurucuAd: null, IkinciTcKimlik: null, IkinciEhliyetNo: null, IkinciEhliyetSinifi: null,
        IkinciEhliyetTarihi: null, IkinciEhliyetYeri: null, IkinciDogumTarihi: null,
        // Araç
        Plaka: "34RENT34", Marka: "Örnek Marka", Tip: "Örnek Model", Grup: "Ekonomik", Yakit: "Benzin", ModelYili: 2024,
        // KM / yakıt / dönüş
        CikisKm: 15000, DonusKm: null, KullanilanKm: null, CikisYakit: 8, DonusYakit: null,
        KmLimit: 450, FazlaKmUcret: 10m,
        KmHediye: null, BitisSebebi: null, TeslimAlanAd: null, GercekDonusTar: null,
        // Tutar dökümü
        GunlukUcret: 900m, Tutar: 2700m, FazlaKmBedeli: 0m, YakitBedeli: 0m, UzatmaBedeli: 0m,
        HediyeGun: null, FaturalananGun: 3, IskontoTutar: null, HaftaSonuFark: null,
        EkHizmetToplam: 150m, GenelToplam: 3000m, Tahsilat: 0m, Bakiye: 3000m, Doviz: "TL",
        Depozito: 2000m, DropUcreti: 0m,
        EkHizmetler: [new SozlesmeEkHizmet("Ek Sürücü (örnek)", 150m)]);
}
