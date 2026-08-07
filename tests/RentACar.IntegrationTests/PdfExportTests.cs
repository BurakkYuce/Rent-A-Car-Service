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
            FirmaMobilTel: "0554 000 00 00", FirmaMarka: "TEST RENT",
            FirmaVergiDairesi: "Kurumlar", FirmaVergiNo: "1234567890", FirmaLogo: TinyPng,
            SozlesmeNo: "RZ-000123", Durum: "Tamamlandi",
            BasTar: new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            BitTar: new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero), Gun: 4,
            CikisOfisi: "MERKEZ", DonusOfisi: null, Aciklama: null,
            MusteriAd: "Deneme Müşteri", MusteriTel: "0532 000 00 00", MusteriEmail: null, MusteriAdres: "Muratpaşa",
            TcKimlik: "10000000146", EhliyetNo: "35030", EhliyetSinifi: "B",
            EhliyetTarihi: new DateTimeOffset(1993, 12, 29, 0, 0, 0, TimeSpan.Zero), EhliyetYeri: "BURDUR",
            DogumTarihi: new DateTimeOffset(1975, 4, 15, 0, 0, 0, TimeSpan.Zero),
            IkinciSurucuAd: "İkinci Sürücü", IkinciTcKimlik: "10000000146", IkinciEhliyetNo: "99999",
            IkinciEhliyetSinifi: "B", IkinciEhliyetTarihi: null, IkinciEhliyetYeri: "ANTALYA", IkinciDogumTarihi: null,
            Plaka: "07BOP605", Marka: "Fiat", Tip: "Egea", Grup: "Ekonomik", Yakit: "Dizel", ModelYili: 2024,
            CikisKm: 70500, DonusKm: 71200, KullanilanKm: 700, CikisYakit: 8, DonusYakit: 6,
            KmLimit: 500, FazlaKmUcret: 15m,
            KmHediye: 100, BitisSebebi: "Normal", TeslimAlanAd: "Onur Yuce", GercekDonusTar: null,
            GunlukUcret: 100m, Tutar: 400m, FazlaKmBedeli: 1500m, YakitBedeli: 70m, UzatmaBedeli: 0m,
            HediyeGun: 1, FaturalananGun: 4, IskontoTutar: 50m, HaftaSonuFark: 30m,
            EkHizmetToplam: 50m, GenelToplam: 2020m, Tahsilat: 0m, Bakiye: 2020m, Doviz: "TL",
            Depozito: 500m, DropUcreti: 0m,
            EkHizmetler: [new RentACar.Application.Bookings.SozlesmeEkHizmet("Bebek Koltuğu", 50m)]);

        var pdf = new PdfExportService().Contract(s);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    /// <summary>Örnek (şablon) sözleşme — gerçek kira gerekmez; RentPro markalı + geçerli PDF.</summary>
    [Fact]
    public void Ornek_sozlesme_pdf_rentpro_ve_gecerli()
    {
        var v = OrnekSozlesme.Ornek();
        Assert.Equal("RentPro", v.FirmaMarka);                 // RentPro markası
        Assert.False(string.IsNullOrWhiteSpace(v.SozlesmeNo)); // örnek sözleşme no dolu
        Assert.Null(v.IkinciSurucuAd);                          // örnek: tek sürücü

        var pdf = new PdfExportService().Contract(v);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    /// <summary>
    /// PR-18 — SÖZLEŞME PDF'İ BOYUT KİLİDİ. Bu ölçüm kalıcı bir regresyon çitidir: çıktı 1,05 MB'dı
    /// ve bunun 995 KB'ı TEK gömülü görseldi (ekspertiz şeması, 2000x647 RGBA, alfası tamamen opak).
    /// Varlık 1040x336 / 16 seviye griye indirildi ve QuestPDF'e <c>UseOriginalImage()</c> ile
    /// veriliyor.
    ///
    /// <para>Üç şeyi birlikte kilitler:</para>
    /// <list type="number">
    ///   <item><b>Boyut</b> — 150 KB üstüne çıkarsa biri varlığı büyütmüş ya da yeniden kodlamayı
    ///   geri açmıştır. PR-C paylaşım anlık görüntüleri bu baytları SAKLIYOR, yani şişme kalıcı olur.</item>
    ///   <item><b>JPEG YOK</b> (<c>/DCTDecode</c>) — şema bir ÇİZGİ ÇİZİMİ; lossy kodlama etiketlerin
    ///   ("Avadanlık", "Trafik Seti") çevresinde halka üretir ve sahada üzerine hasar işaretlenen
    ///   form okunmaz hale gelir.</item>
    ///   <item><b>Sayfa sayısı 1</b> — küçültme düzeni kaydırmadı.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Sozlesme_pdf_boyutu_ve_gorsel_kodlamasi_KILITLI()
    {
        var pdf = new PdfExportService().Contract(OrnekSozlesme.Ornek());
        var metin = System.Text.Encoding.Latin1.GetString(pdf);

        // Bağımsız oracle: eşik koddan değil ölçümden geliyor (öncesi 1.054.980 bayt, sonrası ~101 KB).
        Assert.True(pdf.Length < 150_000,
            $"Sözleşme PDF'i {pdf.Length} bayt — 150 KB eşiğini aştı. Ekspertiz şeması büyütülmüş " +
            "ya da görsel yeniden kodlaması geri açılmış olabilir (bkz. scripts/optimize-ekspertiz-sema.py).");

        Assert.DoesNotContain("/DCTDecode", metin);   // JPEG'e yeniden kodlanmıyor (metin net kalıyor)

        // Gömülü şema: gri tonlama, tek görsel (örnek sözleşmede tenant logosu YOK).
        Assert.Contains("/DeviceGray", metin);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(metin, @"/Subtype\s*/Image"));

        // Düzen kaymadı.
        var sayfa = System.Text.RegularExpressions.Regex.Matches(metin, @"/Type\s*/Page[^s]").Count;
        Assert.Equal(1, sayfa);
    }

    /// <summary>Generic tablo PDF'i (tüm liste/rapor ?format=pdf çıktısı) — geçerli PDF + tip-duyarlı hücreler.</summary>
    [Fact]
    public void Table_pdf_generic_valid_and_nonempty()
    {
        var pdf = new PdfExportService().Table(
            "Test Listesi",
            ["Kod", "Ad", "Tutar", "Tarih", "Aktif"],
            [
                new object?[] { "K1", "Birinci", 1234.5m, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), true },
                new object?[] { "K2", "İkinci", null, null, false }   // null/bool/decimal biçimleme yolu
            ]);
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    [Fact]
    public void Invoice_pdf_markali_gecerli()
    {
        var inv = new Invoice
        {
            No = "FT-000045",
            Tarih = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            NetTutar = 333.33m, KdvTutar = 66.67m, GenelToplam = 400m, Currency = "TRY"
        };
        inv.Lines.Add(new InvoiceLine { Aciklama = "Araç kirası", Miktar = 1m, KdvOrani = 0.20m, SatirToplam = 400m });

        // PR-C: markalı fatura (logo + firma) + cari adı.
        var marka = new PdfMarka(TinyPng, "Test Rent A Ş.", "TEST RENT", "Antalya", "0242 000", "Kurumlar", "1234567890");
        var pdf = new PdfExportService().Invoice(inv, marka, "Deneme Müşteri");
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    [Fact]
    public void TahsilatMakbuzu_pdf_gecerli()
    {
        var tx = new CashTransaction
        {
            No = "TH-000012", Tip = RentACar.Domain.Enums.CashTransactionType.Tahsilat,
            Tarih = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
            Amount = new RentACar.Domain.Common.Money(1500m, "TRY", 1m),
            KarsiHesap = RentACar.Domain.Enums.LedgerAccountType.Kasa, Aciklama = "Kira tahsilatı"
        };
        var marka = new PdfMarka(null, "Test Rent A Ş.", "TEST RENT", "Antalya", "0242 000", "Kurumlar", "1234567890");
        var pdf = new PdfExportService().TahsilatMakbuzu(tx, marka, "Deneme Müşteri");
        Assert.True(pdf.Length > 500);
        Assert.True(IsPdf(pdf));
    }

    // 1x1 saydam PNG (logo render yolunu doğrular — geçerli görsel byte'ları).
    /// <summary>
    /// FAZ-80 — şablonun "fiziksel imza alanı" anahtarı PDF'e gerçekten yansıyor mu.
    ///
    /// <para><b>Oracle neden metin araması değil:</b> QuestPDF içerik akışlarını sıkıştırır ve
    /// yazıtiplerini alt-kümeleyerek glif kimlikleriyle yazar — üretilen baytlarda "İMZA" diye bir
    /// dizi ARAMAK mümkün değil (bulunamaması bir şey kanıtlamazdı). Bunun yerine ölçülebilir ve
    /// yanıltmayan üç iddia kuruluyor: (1) her iki çıktı da geçerli PDF, (2) imza bloğu kapalıyken
    /// çıktı KÜÇÜLÜYOR (blok gerçekten basılmıyor), (3) parametre HİÇ verilmediğinde çıktı, açık
    /// hâlle aynı boyutta — yani varsayılan davranış değişmedi.</para>
    /// </summary>
    [Fact]
    public void Sablon_imza_alani_kapaliysa_sozlesme_PDFi_KUCULUR_varsayilan_DEGISMEZ()
    {
        var svc = new PdfExportService();

        var acik = svc.Contract(Sozlesme(imza: true));
        var kapali = svc.Contract(Sozlesme(imza: false));
        var varsayilan = svc.Contract(OrnekSozlesme.Ornek());   // parametre verilmez → true

        Assert.True(IsPdf(acik));
        Assert.True(IsPdf(kapali));
        Assert.True(kapali.Length < acik.Length,
            $"İmza bloğu kapalıyken PDF küçülmeliydi: açık={acik.Length}, kapalı={kapali.Length}");

        // Varsayılan (şablonsuz) yol AÇIK hâlle aynı içeriği üretmeli — regresyon çiti.
        var varsayilanAcik = svc.Contract(OrnekSozlesme.Ornek() with { SablonImzaAlaniGoster = true });
        Assert.Equal(varsayilanAcik.Length, varsayilan.Length);
        // …ve kapatıldığında o da küçülmeli (örnek sözleşme yolu da anahtarı gerçekten okuyor).
        Assert.True(svc.Contract(OrnekSozlesme.Ornek() with { SablonImzaAlaniGoster = false }).Length
                    < varsayilan.Length);
    }

    /// <summary>İmza anahtarı dışında HER ŞEYİ aynı olan iki sözleşme görünümü (tek değişken).</summary>
    private static RentACar.Application.Bookings.SozlesmeView Sozlesme(bool imza)
        => OrnekSozlesme.Ornek() with { SablonImzaAlaniGoster = imza };

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
