using RentACar.Domain.Entities;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap F4 — PDF export (QuestPDF). BAĞIMSIZ ORACLE: üretilen byte[] geçerli PDF (%PDF imzası) ve dolu.
/// Birim test (DB yok); Community lisansı PdfExportService static ctor'da set edilir (kimliksiz).
/// </summary>
public sealed class PdfExportTests
{
    private static bool IsPdf(byte[] b)
        => b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46; // "%PDF"

    /// <summary>Sözleşme PDF'i artık SozlesmeView'den (HTML-print ile tek kaynak) — km/ehliyet/ekspertiz dahil.</summary>
    [Fact]
    public void Contract_pdf_is_valid_and_nonempty()
    {
        var s = new RentACar.Application.Bookings.SozlesmeView(
            FirmaUnvan: "Test Rent A Car", FirmaAdres: "Antalya", FirmaTel: "0242 000 00 00",
            FirmaVergiDairesi: "Kurumlar", FirmaVergiNo: "1234567890",
            SozlesmeNo: "RZ-000123", Durum: "Tamamlandi",
            BasTar: new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            BitTar: new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero), Gun: 4,
            CikisOfisi: "MERKEZ", DonusOfisi: null, Aciklama: null,
            MusteriAd: "Deneme Müşteri", MusteriTel: "0532 000 00 00", MusteriEmail: null, MusteriAdres: "Muratpaşa",
            TcKimlik: "10000000146", EhliyetNo: "35030", EhliyetSinifi: "B",
            EhliyetTarihi: new DateTimeOffset(1993, 12, 29, 0, 0, 0, TimeSpan.Zero), EhliyetYeri: "BURDUR",
            DogumTarihi: new DateTimeOffset(1975, 4, 15, 0, 0, 0, TimeSpan.Zero),
            Plaka: "07BOP605", Marka: "Fiat", Tip: "Egea", Grup: "Ekonomik", Yakit: "Dizel", ModelYili: 2024,
            CikisKm: 70500, DonusKm: 71200, KullanilanKm: 700, CikisYakit: 8, DonusYakit: 6,
            KmLimit: 500, FazlaKmUcret: 15m,
            KmHediye: 100, BitisSebebi: "Normal", TeslimAlanAd: "Onur Yuce", GercekDonusTar: null,
            GunlukUcret: 100m, Tutar: 400m, FazlaKmBedeli: 1500m, YakitBedeli: 70m, UzatmaBedeli: 0m,
            EkHizmetToplam: 50m, GenelToplam: 2020m, Tahsilat: 0m, Bakiye: 2020m, Doviz: "TL",
            EkHizmetler: [new RentACar.Application.Bookings.SozlesmeEkHizmet("Bebek Koltuğu", 50m)]);

        var pdf = new PdfExportService().Contract(s);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    [Fact]
    public void Invoice_pdf_is_valid_and_nonempty()
    {
        var inv = new Invoice
        {
            No = "FT-000045",
            Tarih = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            NetTutar = 333.33m, KdvTutar = 66.67m, GenelToplam = 400m, Currency = "TRY"
        };
        inv.Lines.Add(new InvoiceLine { Aciklama = "Araç kirası", Miktar = 1m, SatirToplam = 400m });

        var pdf = new PdfExportService().Invoice(inv);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }
}
